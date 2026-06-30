using System;
using System.Collections;
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

        public bool LogBackfillUpdates;
        public bool LogPollingUpdates;

        public Observable<MonitorResponseDTO> Monitor { get; private set; } =
            new Observable<MonitorResponseDTO>() { };
        public Observable<BackfillResponseDTO<A>> Backfill { get; private set; } =
            new Observable<BackfillResponseDTO<A>>() { };

        private protected DeploymentEnvironmentDTO DeploymentEnvs;
        private protected MatchEnvironmentDTO<A> MatchEnvs;
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
            // MTODO
            //check current tickets + TargetPlayerCount
            //api request
            //call TrackTicket(ticket) on success
        }

        public void RemoveBackfill(string backfillID)
        {
            // MTODO
            //check backfills => api request
            //else check currents/on api success => UntrackTicket(backfillID)
        }

        public void RemoveAllBackfills()
        {
            Polling = false;
            // MTODO foreach ticket call RemoveBackfill in parallel
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

            LoadEnvs();
            MatchmakingApi = new Api(Handler, AuthToken, BaseUrl);

            L.SubscribeLogger(Monitor, "MM", "Monitor");
            Monitor.Subscribe(onMonitorUpdate);

            L.SubscribeLogger(Backfill, "MM", "Backfill", LogBackfillUpdates);
            Backfill.Subscribe(onBackfillUpdate);

            Status();
        }
        #endregion

        #region Internals
        internal void LoadEnvs()
        {
            IDictionary envs = Environment.GetEnvironmentVariables();
            DeploymentEnvs = new DeploymentEnvironmentDTO(envs);
            MatchEnvs = new MatchEnvironmentDTO<A>(envs);

            // MTODO foreach ticket TrackTicket(convertedTicket)
        }

        internal void TrackTicket(BackfillTicketMemberDTO<A> ticket)
        {
            // MTODO add ticket to currents
        }

        internal void UntrackTicket(string ticketID)
        {
            // MTODO remove ticket from currents
        }
        #endregion
    }
}
