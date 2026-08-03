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
        public int TargetPlayerCount;

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

        public Server(
            MonoBehaviour handler,
            string baseUrl,
            string authToken,
            int targetPlayerCount = -1,
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
            TargetPlayerCount = targetPlayerCount;

            BaseUrl = baseUrl;
            AuthToken = authToken;

            RequestTimeoutSeconds = requestTimeoutSeconds;
            PollingBackoffSeconds = pollingBackoffSeconds;
            MaxConsecutivePollingErrors = maxConsecutivePollingErrors;

            LogBackfillUpdates = logBackfillUpdates;
            LogPollingUpdates = logPollingUpdates;
        }

        public void RemoveAssignment(string ticketID)
        {
            L.Log($"Removing assigned ticket {ticketID}");
            Assignments.Remove(ticketID);
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
            if (Assignments.Count + Backfills.Current.Count >= TargetPlayerCount)
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
                    Backfills._Update(temp, $"created:{backfillRes.ID}");

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
                Backfills._Error($"{backfillID} not found");
                return;
            }

            MatchmakingApi.DeleteBackfill(
                backfillID,
                (UnityWebRequest request) =>
                {
                    UntrackBackfill(
                        backfillID,
                        $"abandoned:{backfillID}",
                        false,
                        onCompletedDelegate
                    );
                },
                (string error, UnityWebRequest request) =>
                {
                    if (request.responseCode == 404)
                    {
                        UntrackBackfill(
                            backfillID,
                            $"{backfillID} abandon failed (not found)",
                            false,
                            onCompletedDelegate
                        );
                    }
                    else
                    {
                        UntrackBackfill(
                            backfillID,
                            $"{backfillID} abandon failed\n{error}",
                            true,
                            onCompletedDelegate
                        );
                    }
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
                    RemoveBackfillRoutine(
                        b.Key,
                        () =>
                        {
                            if (onCompletedDelegate is not null && Backfills.Current.Count == 0)
                            {
                                onCompletedDelegate();
                            }
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
            Backfills._Update(new Dictionary<string, BackfillResponseDTO<A>>(), "initializing");

            foreach (var t in tickets)
            {
                AddAssignment(t.Value);
            }

            Status();
        }
        #endregion

        #region Internals

        internal void UntrackBackfill(
            string backfillID,
            string msg,
            bool error,
            Action onCompletedDelegate = null
        )
        {
            Dictionary<string, BackfillResponseDTO<A>> temp = new Dictionary<
                string,
                BackfillResponseDTO<A>
            >(Backfills.Current);
            temp.Remove(backfillID);

            if (error)
            {
                Backfills._Error(msg, temp);
            }
            else
            {
                Backfills._Update(temp, msg);
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
                        UntrackBackfill(backfillID, $"backfill {backfillID} assigned", false);
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
                            $"polling {backfillID} failed, reached maximum retries\n{error}"
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
                        else
                        {
                            Backfills._Error($"polling {backfillID} failed\n{error}");
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

        internal IEnumerator RemoveBackfillRoutine(
            string backfillID,
            Action onCompletedDelegate = null
        )
        {
            L.Log($"Removing backfill {backfillID}");
            RemoveBackfill(backfillID, onCompletedDelegate);
            yield return null;
        }
        #endregion
    }
}
