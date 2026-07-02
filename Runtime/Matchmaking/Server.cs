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

    public class Server<B, A>
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
        public Observable<BackfillResponseDTO<A>> Backfills { get; private set; } =
            new Observable<BackfillResponseDTO<A>>() { };

        public DeploymentEnvironmentDTO DeploymentEnvs { get; private set; }
        public MatchEnvironmentDTO<A> MatchEnvs { get; private set; }

        public Dictionary<string, InjectedTicketDTO<A>> AssignedTickets { get; private set; } =
            new Dictionary<string, InjectedTicketDTO<A>>();

        public Dictionary<string, BackfillResponseDTO<A>> OngoingBackfills { get; private set; } =
            new Dictionary<string, BackfillResponseDTO<A>>();

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

        public void RemoveAssignedTicket(string ticketID)
        {
            AssignedTickets.Remove(ticketID);
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
            if (AssignedTickets.Count + OngoingBackfills.Count >= TargetPlayerCount)
            {
                Backfills._Error("maximum capacity currently reached");
                return;
            }

            MatchmakingApi.CreateBackfill<B, A>(
                backfill,
                (BackfillResponseDTO<A> backfillRes, UnityWebRequest request) =>
                {
                    OngoingBackfills[backfillRes.ID] = backfillRes;
                    Backfills._Update(backfillRes, "created");
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
            if (!OngoingBackfills.ContainsKey(backfillID))
            {
                Backfills._Error($"{backfillID} not found");
                return;
            }

            MatchmakingApi.DeleteBackfill(
                backfillID,
                (UnityWebRequest request) =>
                {
                    Backfills._Update(null, $"{backfillID} abandoned");
                    RemoveOngoingBackfill(backfillID);

                    if (onCompletedDelegate is not null)
                    {
                        onCompletedDelegate();
                    }
                },
                (string error, UnityWebRequest request) =>
                {
                    if (request.responseCode == 404)
                    {
                        Backfills._Update(null, $"{backfillID} abandon failed (not found)");
                        RemoveOngoingBackfill(backfillID);
                    }
                    else
                    {
                        Backfills._Error($"{backfillID} abandon failed\n{error}");
                    }

                    if (onCompletedDelegate is not null)
                    {
                        onCompletedDelegate();
                    }
                }
            );
        }

        public void RemoveAllBackfills()
        {
            Polling = false;
            //MTODO foreach ticket call RemoveBackfill in parallel
        }
        #endregion

        #region Initialization
        public void Initialize(
            UnityAction<
                Observable<MonitorResponseDTO>,
                ObservableActionType,
                string
            > onMonitorUpdate,
            UnityAction<
                Observable<BackfillResponseDTO<A>>,
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

            LoadEnvs();
            foreach (KeyValuePair<string, InjectedTicketDTO<A>> t in MatchEnvs.Tickets)
            {
                AddAssignedTicket(t.Value);
            }

            Status();
        }
        #endregion

        #region Internals
        internal void LoadEnvs()
        {
            IDictionary envs = Environment.GetEnvironmentVariables();
            DeploymentEnvs = new DeploymentEnvironmentDTO(envs);
            MatchEnvs = new MatchEnvironmentDTO<A>(envs);
        }

        internal void AddAssignedTicket(InjectedTicketDTO<A> ticket)
        {
            AssignedTickets[ticket.ID] = ticket;
        }

        internal void RemoveOngoingBackfill(string backfillID)
        {
            OngoingBackfills.Remove(backfillID);
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
                (BackfillResponseDTO<A> backfillRes, UnityWebRequest request) =>
                {
                    if (backfillRes.Status == "ASSIGNED")
                    {
                        AddAssignedTicket(backfillRes.AssignedTicket);
                        RemoveOngoingBackfill(backfillID);
                        Backfills._Update(backfillRes, $"{backfillID} assigned");
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
        #endregion
    }
}
