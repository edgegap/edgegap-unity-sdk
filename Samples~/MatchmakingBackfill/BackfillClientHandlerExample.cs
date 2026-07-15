using System.Collections.Generic;
using Edgegap;
using Edgegap.Matchmaking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using L = Edgegap.Logger;
using MyGroupUpRequestDTO = Edgegap.Matchmaking.BackfillGroupUpRequestDTO;
using MyTicketsAttributes = Edgegap.Matchmaking.BackfillTicketAttributesDTO;

// todo replace BackfillTicketAttributesDTO with CustomTicketsAttributes
// todo replace BackfillGroupUpRequestDTO with CustomGroupUpRequestDTO

public class BackfillClientHandlerExample : MonoBehaviour
{
    public static BackfillClientHandlerExample Instance { get; private set; }

    #region Matchmaking Configuration

    [Header("Matchmaker Instance")]
    public string BaseUrl;
    public string AuthToken;
    public string[] BackfillGroupSize = { "new", "1" };

    [Header("Exponential Retry")]
    public int RequestTimeoutSeconds = 3;
    public float PollingBackoffSeconds = 1f;
    public int MaxConsecutivePollingErrors = 10;

    [Header("Expiration and Cleanup")]
    public float RemoveAssignmentSeconds = 30f;
    public bool DeleteGroupOnPause = false;
    public bool DeleteGroupOnQuit = true;

    [Header("Logging")]
    public bool LogGroupUpdates = true;
    public bool LogPollingUpdates = false;
    #endregion

    public GroupClient<
            MyGroupUpRequestDTO,
            MyTicketsAttributes
        > MatchmakingClient;

    #region UI
    [Header("UI")]
    public Text StatusDisplay;
    public Button DisconnectButton;

    private string StatusDisplayDefaultPath = "/Canvas/StatusTxt";
    private string DisconnectBtnDefaultPath = "/Canvas/DisconnectBtn";
    #endregion

    private string TicketID;

#if !UNITY_SERVER
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
        }
        else
        {
            Instance = this;

            if (StatusDisplay == null)
            {
                L.Log("MM ClientHandler | No Status Display provided, using default.");
                StatusDisplay = GameObject.Find(StatusDisplayDefaultPath)?.GetComponent<Text>();

                if (StatusDisplay == null)
                {
                    L.Warn(
                        $"MM ClientHandler | Unable to find default component {StatusDisplayDefaultPath} in scene."
                    );
                }
                else
                {
                    StatusDisplay.text = "";
                }
            }

            if (DisconnectButton == null)
            {
                L.Log("MM ClientHandler | Disconnect Button provided, using default.");
                DisconnectButton = GameObject.Find(DisconnectBtnDefaultPath)?.GetComponent<Button>();

                if (DisconnectButton == null)
                {
                    L.Warn(
                        $"MM ClientHandler | Unable to find default component {DisconnectBtnDefaultPath} in scene."
                    );
                }
                else
                {
                    DisconnectButton.onClick.AddListener(Disconnect);
                    DisconnectButton.gameObject.SetActive(false);
                }
            }
        }
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // configure Matchmaking
        MatchmakingClient = new GroupClient<
            MyGroupUpRequestDTO,
            MyTicketsAttributes
        >(
            this,
            BaseUrl,
            AuthToken,
            RequestTimeoutSeconds,
            PollingBackoffSeconds,
            MaxConsecutivePollingErrors,
            RemoveAssignmentSeconds,
            LogGroupUpdates,
            LogPollingUpdates
        );

        // initialize Matchmaking
        MatchmakingClient.Initialize(
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
                        // todo update UI
                        StatusDisplay.text = "Fetching beacons...";

                        MatchmakingClient.Beacons(
                            (BeaconsResponseDTO beacons) =>
                            {
                                Debug.Log($"beacons: {beacons}");

                                MatchmakingClient.MeasureBeaconsRoundTripTime(
                                    beacons.Beacons,
                                    (Dictionary<string, float> pings) =>
                                    {
                                        StatusDisplay.text = "Starting matchmaking.";
                                        StartMatchmaking(pings, true);
                                    }
                                );
                            },
                            (string error, UnityWebRequest request) =>
                            {
                                // todo handle beacon downtime, create tickets without beacons?
                                StatusDisplay.text = "Beacon downtime.";
                                L.Log($"beacon error: {request}");
                            }
                        );
                    }
                    else if (message != "healthy")
                    {
                        // todo handle outage/maintenance
                        StatusDisplay.text = "Matchmaking error.";
                        L.Error($"Matchmaking error.\n{monitor.Current}");
                        MatchmakingClient.StopMatchmaking();
                    }
                }
            },
            // handle group assignment
            (
                Observable<GroupUpResponseDTO> group,
                ObservableActionType action,
                string message
            ) =>
            {
                if (
                    action == ObservableActionType.Update
                    && (
                        message.Contains("created")
                        || message.Contains("joined")
                        || message.Contains("updated")
                        || message.Contains("abandon")
                    )
                )
                {
                    // todo update UI
                }

                if (
                    action == ObservableActionType.Update
                    && message.Contains("updated")
                    && group.Current.Status == "TEAM_FOUND"
                )
                {
                    StatusDisplay.text = "Team found, awaiting match.";
                }

                if (
                    action == ObservableActionType.Update
                    && message.Contains("updated")
                    && group.Current.Status == "MATCH_FOUND"
                )
                {
                    StatusDisplay.text = "Match found, awaiting assignment.";
                }

                if (
                    action == ObservableActionType.Update
                    && message.Contains("updated")
                    && group.Current.Status == "HOST_ASSIGNED"
                )
                {
                    // todo join game on pre-defined game port
                    StatusDisplay.text = $"Host assigned, joining game.\n{group.Current.Assignment.Fqdn}";
                    TicketID = group.Current.TicketID;
                    L.Log($"joining game: {group.Current.Assignment.Ports["gameport"].Link}");
                }
            }
        );
    }

    public void OnApplicationPause(bool pause)
    {
        if (!DeleteGroupOnPause || MatchmakingClient.Group.Current is null)
            return;
        StopMatchmaking();
    }

    public void OnApplicationQuit()
    {
        if (!DeleteGroupOnQuit)
            return;
        StopMatchmaking();
    }

    public void StartMatchmaking(Dictionary<string, float> pings, bool isReady)
    {
        MatchmakingClient.CreateGroup(new MyGroupUpRequestDTO(pings, BackfillGroupSize, isReady));
    }

    public void StopMatchmaking()
    {
        if (enabled)
        {
            MatchmakingClient.StopMatchmaking();
        }
    }

    public void Disconnect()
    {
        // todo notify server with player's ticket ID, then disconnect once processed
        L.Log($"Player {TicketID} leaving game");
    }
#endif
}
