using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;
using Random = UnityEngine.Random;

namespace Edgegap.Matchmaking
{
    using L = Logger;

    public class ServerAgent<B, A>
        where B : BackfillRequestDTO<A>, new()
    {
        private Api MatchmakingApi;

        public MonoBehaviour Handler;
        public string Profile;
        public BackfillAttributes BackfillAttributes;
        public int TargetTeamSize;

        // BaseUrl may only be set with constructor
        public string BaseUrl { get; }
        public string AuthToken { private get; set; }

        public int RequestTimeoutSeconds;
        public float PollingBackoffSeconds;
        public float ExpirationPeriodSeconds;
        public float ConnectionGracePeriodSeconds;

        public bool LogBackfillUpdates;
        public bool LogPollingUpdates;

        public Observable<MonitorResponseDTO> Monitor { get; private set; } =
            new Observable<MonitorResponseDTO>() { };

        public Observable<Dictionary<string, BackfillResponseDTO<A>>> Backfills
        {
            get;
            private set;
        } = new Observable<Dictionary<string, BackfillResponseDTO<A>>>() { };

        public Dictionary<string, BackfillAssignedTicket<A>> Assignments { get; private set; } =
            new Dictionary<string, BackfillAssignedTicket<A>>() { };

        private protected bool Polling = false;

        public ServerAgent(
            MonoBehaviour handler,
            string baseUrl,
            string authToken,
            string profile,
            BackfillAttributes attributes,
            int targetTeamSize = -1,
            int requestTimeoutSeconds = 3,
            float pollingBackoffSeconds = 1f,
            float expirationPeriodSeconds = 30f,
            float connectionGracePeriodSeconds = 60f,
            bool logBackfillUpdates = true,
            bool logPollingUpdates = false
        )
        {
            if (handler == null)
            {
                throw new Exception("MatchmakingServer Handler not assigned.");
            }

            Handler = handler;

            BaseUrl = baseUrl;
            AuthToken = authToken;
            Profile = profile;
            BackfillAttributes = attributes;
            TargetTeamSize = targetTeamSize;

            RequestTimeoutSeconds = requestTimeoutSeconds;
            PollingBackoffSeconds = pollingBackoffSeconds;
            ExpirationPeriodSeconds = expirationPeriodSeconds;
            ConnectionGracePeriodSeconds = connectionGracePeriodSeconds;

            LogBackfillUpdates = logBackfillUpdates;
            LogPollingUpdates = logPollingUpdates;
        }

        public void AbandonPlayer(string ticketID)
        {
            if (Assignments.Remove(ticketID))
            {
                L.Log($"MM | Backfill - ticket removed [{ticketID}]");
                StartNewBackfill();
            }
            else
            {
                L.Warn($"MM | Backfill - ticket abandon failed [{ticketID}]");
            }
        }

        public BackfillAssignedTicket<A> PlayerConnected(string ticketID)
        {
            if (Assignments.ContainsKey(ticketID))
            {
                if (Assignments[ticketID].JoinedAt is null)
                {
                    Assignments[ticketID].JoinedAt = DateTime.Now;
                }

                return Assignments[ticketID];
            }
            else
            {
                return null;
            }
        }

        #region Server API

        public void Status()
        {
            MatchmakingApi.GetMonitor(
                (MonitorResponseDTO monitor, UnityWebRequest request) =>
                {
                    if (monitor.Status.ToLower() == "healthy")
                    {
                        Monitor._Update(monitor, "healthy");
                    }
                    else
                    {
                        Monitor._Update(monitor, "unhealthy");
                    }
                },
                (string error, UnityWebRequest request) =>
                {
                    Monitor._Error($"get monitor failed (unexpected error)\n{error}", null);
                }
            );
        }

        public void AddBackfill(B backfill)
        {
            if (Assignments.Count + Backfills.Current.Count >= TargetTeamSize)
            {
                Backfills._Error("maximum capacity currently reached");
                return;
            }

            MatchmakingApi.CreateBackfill<B, A>(
                backfill,
                (BackfillResponseDTO<A> backfillRes, UnityWebRequest request) =>
                {
                    backfillRes.CreatedAt = DateTime.Now;

                    Dictionary<string, BackfillResponseDTO<A>> temp = new Dictionary<
                        string,
                        BackfillResponseDTO<A>
                    >(Backfills.Current);

                    temp[backfillRes.ID] = backfillRes;
                    Backfills._Update(temp, $"created [{backfillRes.ID}]");

                    if (!Polling && Backfills.Current.Count == 1)
                    {
                        Polling = true;
                        Handler.StartCoroutine(DelayMethod(StartPollingBackfills));
                    }
                },
                (string error, UnityWebRequest request) =>
                {
                    Backfills._Error($"backfill create failed\n{error}");
                }
            );
        }

        public void AddBackfills()
        {
            int nbBackfills = Assignments.Count + Backfills.Current.Count - TargetTeamSize;

            for (int i = 0; i < nbBackfills; ++i)
            {
                StartNewBackfill();
            }
        }

        public void RemoveBackfill(string backfillID, Action onCompletedDelegate = null)
        {
            if (!Backfills.Current.ContainsKey(backfillID))
            {
                Backfills._Notify(
                    $"delete failed (not found) [{backfillID}]",
                    ObservableActionType.Warn
                );
                return;
            }

            MatchmakingApi.DeleteBackfill(
                backfillID,
                (UnityWebRequest request) =>
                {
                    Handler.StartCoroutine(
                        ExpireBackfill(
                            backfillID,
                            (
                                bool backfillExpired,
                                Dictionary<string, BackfillResponseDTO<A>> updatedBackfills
                            ) =>
                            {
                                OnBackfillExpired(
                                    backfillID,
                                    backfillExpired,
                                    updatedBackfills,
                                    () =>
                                    {
                                        Backfills._Update(
                                            updatedBackfills,
                                            $"abandoned [{backfillID}]"
                                        );
                                    },
                                    onCompletedDelegate
                                );
                            }
                        )
                    );
                },
                (string error, UnityWebRequest request) =>
                {
                    Handler.StartCoroutine(
                        ExpireBackfill(
                            backfillID,
                            (
                                bool backfillExpired,
                                Dictionary<string, BackfillResponseDTO<A>> updatedBackfills
                            ) =>
                            {
                                OnBackfillExpired(
                                    backfillID,
                                    backfillExpired,
                                    updatedBackfills,
                                    () =>
                                    {
                                        if (request.responseCode == 404)
                                        {
                                            Backfills._Notify(
                                                $"abandon failed (not found) [{backfillID}]",
                                                ObservableActionType.Warn
                                            );
                                        }
                                        else
                                        {
                                            Backfills._Error(
                                                $"abandon failed [{backfillID}]\n{error}",
                                                updatedBackfills
                                            );
                                        }
                                    },
                                    onCompletedDelegate
                                );
                            }
                        )
                    );
                }
            );
        }

        public void RemoveAllBackfills(Action onCompletedDelegate = null)
        {
            Polling = false;
            Dictionary<string, BackfillResponseDTO<A>> temp = new Dictionary<
                string,
                BackfillResponseDTO<A>
            >(Backfills.Current);

            foreach (KeyValuePair<string, BackfillResponseDTO<A>> b in temp)
            {
                Handler.StartCoroutine(
                    ExpireBackfill(
                        b.Key,
                        (
                            bool backfillExpired,
                            Dictionary<string, BackfillResponseDTO<A>> updatedBackfills
                        ) =>
                        {
                            OnBackfillExpired(
                                b.Key,
                                backfillExpired,
                                updatedBackfills,
                                () =>
                                {
                                    Backfills._Update(updatedBackfills, $"abandoned [{b.Key}]");
                                },
                                () =>
                                {
                                    if (
                                        onCompletedDelegate is not null
                                        && Backfills.Current.Count == 0
                                    )
                                    {
                                        onCompletedDelegate();
                                    }
                                }
                            );
                        }
                    )
                );
            }
        }
        #endregion

        #region Initialization
        public void Initialize(
            Dictionary<string, InjectedTicketDTO<A>> tickets,
            UnityAction<
                Observable<MonitorResponseDTO>,
                ObservableActionType,
                string
            > onMonitorUpdate,
            UnityAction<
                Observable<Dictionary<string, BackfillResponseDTO<A>>>,
                ObservableActionType,
                string
            > onBackfillUpdate
        )
        {
            if (string.IsNullOrEmpty(BaseUrl.Trim()))
            {
                throw new Exception("BaseUrl not declared.");
            }

            if (string.IsNullOrEmpty(AuthToken.Trim()))
            {
                throw new Exception("AuthToken not declared.");
            }

            MatchmakingApi = new Api(Handler, AuthToken, BaseUrl);

            L.SubscribeLogger(Monitor, "MM", "Monitor");
            Monitor.Subscribe(onMonitorUpdate);

            L.SubscribeLogger(Backfills, "MM", "Backfill", LogBackfillUpdates);
            Backfills.Subscribe(onBackfillUpdate);

            foreach (InjectedTicketDTO<A> t in tickets.Values)
            {
                AddAssignment(t);
            }

            Status();
        }
        #endregion

        #region Internals

        internal IEnumerator ExpireBackfill(
            string backfillID,
            Action<bool, Dictionary<string, BackfillResponseDTO<A>>> onCompletedDelegate,
            float delaySeconds = 0f
        )
        {
            yield return new WaitForSeconds(delaySeconds);

            Dictionary<string, BackfillResponseDTO<A>> temp = new Dictionary<
                string,
                BackfillResponseDTO<A>
            >(Backfills.Current);

            onCompletedDelegate(temp.Remove(backfillID), temp);
        }

        internal void OnBackfillExpired(
            string backfillID,
            bool backfillExpired,
            Dictionary<string, BackfillResponseDTO<A>> updatedBackfills,
            Action onExpiredSuccess,
            Action onCompletedDelegate = null
        )
        {
            if (backfillExpired)
            {
                onExpiredSuccess();
            }
            else
            {
                Backfills._Notify($"expiration failed [{backfillID}]", ObservableActionType.Warn);
            }

            if (onCompletedDelegate is not null)
            {
                onCompletedDelegate();
            }
        }

        internal void AddAssignment(InjectedTicketDTO<A> ticket)
        {
            BackfillAssignedTicket<A> assignment = new BackfillAssignedTicket<A>()
            {
                ID = ticket.ID,
                CreatedAt = ticket.CreatedAt,
                PlayerIP = ticket.PlayerIP,
                GroupID = ticket.GroupID,
                Attributes = ticket.Attributes,
                AssignedAt = DateTime.Now,
            };

            Assignments[assignment.ID] = assignment;
            Handler.StartCoroutine(DelayMethod(() => CheckTicketConnection(assignment.ID)));
        }

        internal void StartNewBackfill()
        {
            B newBackfill = new B()
            {
                Profile = Profile,
                Attributes = BackfillAttributes,
                Tickets = Assignments,
            };

            AddBackfill(newBackfill);
        }

        internal void StartPollingBackfills()
        {
            if (!Polling)
            {
                if (LogPollingUpdates)
                {
                    Backfills._Notify($"polling stopped");
                }
                return;
            }

            foreach (BackfillResponseDTO<A> b in Backfills.Current.Values)
            {
                MatchmakingApi.GetBackfill<A>(
                    b.ID,
                    (BackfillResponseDTO<A> backfill, UnityWebRequest request) =>
                    {
                        if (backfill.Status == "ASSIGNED")
                        {
                            AddAssignment(backfill.AssignedTicket);

                            Handler.StartCoroutine(
                                ExpireBackfill(
                                    b.ID,
                                    (
                                        bool backfillExpired,
                                        Dictionary<string, BackfillResponseDTO<A>> updatedBackfills
                                    ) =>
                                    {
                                        OnBackfillExpired(
                                            b.ID,
                                            backfillExpired,
                                            updatedBackfills,
                                            () =>
                                            {
                                                Backfills._Update(
                                                    updatedBackfills,
                                                    $"assigned [{b.ID}]"
                                                );
                                            }
                                        );
                                    }
                                )
                            );
                        }
                    },
                    (string error, UnityWebRequest request) =>
                    {
                        if ((DateTime.Now - b.CreatedAt)?.TotalSeconds >= ExpirationPeriodSeconds)
                        {
                            Backfills._Notify(
                                $"backfill expiration period exceeded [{b.ID}]",
                                ObservableActionType.Warn
                            );
                            RemoveBackfill(b.ID, StartNewBackfill);
                        }
                        else if (request.responseCode == 404)
                        {
                            Backfills._Notify(
                                $"polling failed (not found) [{b.ID}]",
                                ObservableActionType.Warn
                            );

                            Handler.StartCoroutine(
                                ExpireBackfill(
                                    b.ID,
                                    (
                                        bool backfillExpired,
                                        Dictionary<string, BackfillResponseDTO<A>> updatedBackfills
                                    ) =>
                                    {
                                        OnBackfillExpired(
                                            b.ID,
                                            backfillExpired,
                                            updatedBackfills,
                                            () =>
                                            {
                                                Backfills._Update(
                                                    updatedBackfills,
                                                    $"abandoned [{b.ID}]"
                                                );
                                            },
                                            StartNewBackfill
                                        );
                                    }
                                )
                            );
                        }
                        else if (request.responseCode != 429 && request.responseCode < 500)
                        {
                            Backfills._Error($"polling failed [{b.ID}]\n{error}");
                        }
                    }
                );
            }

            Handler.StartCoroutine(DelayMethod(StartPollingBackfills));
        }

        internal void CheckTicketConnection(string ticketID)
        {
            double? timeSinceAssigned = (
                DateTime.Now - Assignments[ticketID].AssignedAt
            )?.TotalSeconds;

            if (
                Assignments[ticketID].JoinedAt is null
                && timeSinceAssigned >= ConnectionGracePeriodSeconds
            )
            {
                L.Log($"MM | Backfill - connection grace period expired [{ticketID}]");
                AbandonPlayer(ticketID);
            }
            else
            {
                Handler.StartCoroutine(DelayMethod(() => CheckTicketConnection(ticketID)));
            }
        }

        internal IEnumerator DelayMethod(Action onDelayFinished)
        {
            yield return new WaitForSeconds(PollingBackoffSeconds + (0.1f * Random.value));
            onDelayFinished();
        }
        #endregion
    }
}
