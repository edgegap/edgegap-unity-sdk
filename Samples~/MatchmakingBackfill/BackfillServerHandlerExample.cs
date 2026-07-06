using Edgegap;
using Edgegap.Matchmaking;
using UnityEngine;
using L = Edgegap.Logger;
using MyBackfillRequestDTO = Edgegap.Matchmaking.SimpleBackfillRequestDTO;
using MyTicketsAttributes = Edgegap.Matchmaking.BackfillTicketAttributesDTO;

// todo replace BackfillTicketAttributesDTO with CustomTicketsAttributes
// todo replace SimpleBackfillRequestDTO with CustomBackfillRequestDTO

public class BackfillServerHandlerExample : MonoBehaviour
{
    public static BackfillServerHandlerExample Instance { get; private set; }

    #region Matchmaking Configuration

    [Header("Matchmaker Instance")]
    public string BaseUrl;
    public string AuthToken;
    public int TargetPlayerCount = -1;

    [Header("Exponential Retry")]
    public int RequestTimeoutSeconds = 3;
    public float PollingBackoffSeconds = 1f;
    public int MaxConsecutivePollingErrors = 10;

    [Header("Expiration and Cleanup")]
    public bool DeleteBackfillOnQuit = true;

    [Header("Logging")]
    public bool LogBackfillUpdates = true;
    public bool LogPollingUpdates = false;
    #endregion

    public Server<
            MyBackfillRequestDTO,
            MyTicketsAttributes
        > MatchmakingServer;

    private bool BackfillRunning = false;
    private int OngoingRequests = 0;
    private BackfillAttributes BackfillAttributes;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        MatchmakingServer = new Server<
            MyBackfillRequestDTO,
            MyTicketsAttributes
        >(
            this,
            BaseUrl,
            AuthToken,
            TargetPlayerCount,
            RequestTimeoutSeconds,
            PollingBackoffSeconds,
            MaxConsecutivePollingErrors,
            LogBackfillUpdates,
            LogPollingUpdates
        );

        MatchmakingServer.Initialize(
            // handle service monitoring
            (
                Observable<MonitorResponseDTO> monitor,
                ObservableActionType action,
                string message
            ) =>
            {
                if (action == ObservableActionType.Update)
                {
                    if (message == "healthy")
                    {
                        BackfillRunning = true;
                    }
                    else if (message != "healthy")
                    {
                        // todo handle outage/maintenance
                        L.Error($"Matchmaking error.\n{monitor.Current}");
                        StopBackfill();
                    }
                }
            },
            // handle backfill assignment
            (
                Observable<BackfillResponseDTO<MyTicketsAttributes>> backfill,
                ObservableActionType action,
                string message
            ) =>
            {
                if (
                    (action == ObservableActionType.Error && message.Contains("create failed"))
                    || (action == ObservableActionType.Update && message.Contains("assigned"))
                )
                {
                    --OngoingRequests;
                }
            }
        );

        BackfillAttributes = new BackfillAttributes(MatchmakingServer.DeploymentEnvs.Deployment);
    }

    // Update is called once per frame
    void Update()
    {
        if (
            BackfillRunning
            && MatchmakingServer.AssignedTickets.Count + OngoingRequests < TargetPlayerCount
        )
        {
            StartNewBackfill();
        }

        // todo check for leaving players => MatchmakingServer.RemoveAssignedTicket(ticketID);
    }

    public void OnApplicationQuit()
    {
        if (!DeleteBackfillOnQuit)
            return;
        StopBackfill();
    }

    public void StartNewBackfill()
    {
        ++OngoingRequests;

        MyBackfillRequestDTO backfill = new MyBackfillRequestDTO(
            MatchmakingServer.MatchEnvs.MatchProfile,
            BackfillAttributes,
            MatchmakingServer.AssignedTickets
        );

        MatchmakingServer.AddBackfill(backfill);
    }

    public void StopBackfill()
    {
        BackfillRunning = false;
        MatchmakingServer.RemoveAllBackfills();
    }
}
