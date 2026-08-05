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
        where B : BackfillRequestDTO<A>
    {
        private Api MatchmakingApi;

        public MonoBehaviour Handler;
        public int TargetTeamSize;

        // BaseUrl may only be set with constructor
        public string BaseUrl { get; }
        public string AuthToken { private get; set; }

        public int RequestTimeoutSeconds;
        public float PollingBackoffSeconds;
        public int MaxConsecutivePollingErrors;
        public float RemoveBackfillSeconds;

        public bool LogBackfillUpdates;
        public bool LogPollingUpdates;

        public Observable<MonitorResponseDTO> Monitor { get; private set; } =
            new Observable<MonitorResponseDTO>() { };

        public Observable<Dictionary<string, BackfillResponseDTO<A>>> Backfills
        {
            get;
            private set;
        } = new Observable<Dictionary<string, BackfillResponseDTO<A>>>() { };

        public Dictionary<string, InjectedTicketDTO<A>> Assignments { get; private set; } =
            new Dictionary<string, InjectedTicketDTO<A>>() { };

        private protected bool Polling = false;

        public ServerAgent(
            MonoBehaviour handler,
            string baseUrl,
            string authToken,
            int targetTeamSize = -1,
            int requestTimeoutSeconds = 3,
            float pollingBackoffSeconds = 1f,
            int maxConsecutivePollingErrors = 10,
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
            TargetTeamSize = targetTeamSize;

            RequestTimeoutSeconds = requestTimeoutSeconds;
            PollingBackoffSeconds = pollingBackoffSeconds;
            MaxConsecutivePollingErrors = maxConsecutivePollingErrors;

            LogBackfillUpdates = logBackfillUpdates;
            LogPollingUpdates = logPollingUpdates;
        }

        public bool PlayerAbandoned(string ticketID)
        {
            L.Log($"MM | Backfill - removing ticket [{ticketID}]");
            return Assignments.Remove(ticketID);
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
                    Dictionary<string, BackfillResponseDTO<A>> temp = new Dictionary<
                        string,
                        BackfillResponseDTO<A>
                    >(Backfills.Current);

                    temp[backfillRes.ID] = backfillRes;
                    Backfills._Update(temp, $"created [{backfillRes.ID}]");

                    Polling = true;
                    Handler.StartCoroutine(DelayPollingBackfill(backfillRes.ID));
                },
                (string error, UnityWebRequest request) =>
                {
                    Backfills._Error($"backfill create failed\n{error}");
                }
            );
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
            Assignments[ticket.ID] = ticket;
        }

        internal void StartPollingBackfill(string backfillID, int consecutiveErrors = 0)
        {
            if (!Polling)
            {
                if (LogPollingUpdates)
                {
                    Backfills._Notify($"polling {backfillID} stopped");
                }
                return;
            }

            if (LogPollingUpdates)
            {
                Backfills._Notify(
                    $"polling {backfillID} [{consecutiveErrors + 1}/{MaxConsecutivePollingErrors}]"
                );
            }

            MatchmakingApi.GetBackfill<A>(
                backfillID,
                (BackfillResponseDTO<A> backfill, UnityWebRequest request) =>
                {
                    if (backfill.Status == "ASSIGNED")
                    {
                        AddAssignment(backfill.AssignedTicket);

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
                                                $"assigned [{backfillID}]"
                                            );
                                        }
                                    );
                                }
                            )
                        );
                    }
                    else
                    {
                        Handler.StartCoroutine(DelayPollingBackfill(backfillID));
                    }
                },
                (string error, UnityWebRequest request) =>
                {
                    if (consecutiveErrors + 1 > MaxConsecutivePollingErrors)
                    {
                        Backfills._Error(
                            $"polling failed (reached maximum retries) [{backfillID}]\n{error}"
                        );
                        RemoveBackfill(backfillID);
                    }
                    else
                    {
                        if (request.responseCode == 429 || request.responseCode >= 500)
                        {
                            Handler.StartCoroutine(
                                DelayPollingBackfill(backfillID, consecutiveErrors + 1)
                            );
                        }
                        else if (request.responseCode == 404)
                        {
                            Backfills._Notify(
                                $"polling failed (not found) [{backfillID}]",
                                ObservableActionType.Warn
                            );

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
                                            }
                                        );
                                    }
                                )
                            );
                        }
                        else
                        {
                            Backfills._Error($"polling failed [{backfillID}]\n{error}");
                            RemoveBackfill(backfillID);
                        }
                    }
                }
            );
        }

        internal IEnumerator DelayPollingBackfill(string backfillID, int consecutiveErrors = 0)
        {
            yield return new WaitForSeconds(PollingBackoffSeconds + (0.1f * Random.value));
            StartPollingBackfill(backfillID, consecutiveErrors);
        }
        #endregion
    }
}
