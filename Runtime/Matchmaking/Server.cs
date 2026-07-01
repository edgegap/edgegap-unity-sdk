using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

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
                    //MTODO delay polling new backfill
                },
                (string error, UnityWebRequest request) =>
                {
                    Backfills._Error($"backfill create failed\n{error}");
                }
            );
        }

        public void GetBackfill(string backfillID)
        {
            //MTODO
        }

        public void RemoveBackfill(string backfillID, Action onCompletedDelegate = null)
        {
            if (!OngoingBackfills.ContainsKey(backfillID))
            {
                Backfills._Error("backfill not found");
                return;
            }

            MatchmakingApi.DeleteBackfill(
                backfillID,
                (UnityWebRequest request) =>
                {
                    Backfills._Update(null, "abandoned");

                    if (onCompletedDelegate is not null)
                    {
                        onCompletedDelegate();
                    }
                },
                (string error, UnityWebRequest request) =>
                {
                    if (request.responseCode == 404)
                    {
                        Backfills._Update(null, $"abandon failed (ID {backfillID} not found)");
                    }
                    else
                    {
                        Backfills._Error($"abandon failed\n{error}");
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
        #endregion
    }
}
