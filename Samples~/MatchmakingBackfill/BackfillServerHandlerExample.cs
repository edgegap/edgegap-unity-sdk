using System;
using System.Collections;
using System.Collections.Generic;
using Edgegap;
using Edgegap.Matchmaking;
using UnityEngine;
using UnityEngine.Networking;
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

    public ServerAgent<
            MyBackfillRequestDTO,
            MyTicketsAttributes
        > MatchmakingServer;

    private bool BackfillRunning = false;
    private int UnprocessedBackfills = 0;
    private BackfillAttributes BackfillAttributes;
    private SafeHttpRequest Request;

    public DeploymentEnvironmentDTO DeploymentEnvs { get; private set; }

    public MatchEnvironmentDTO<MyTicketsAttributes> MatchEnvs { get; private set; }

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
        if (!Application.isBatchMode)
        {
            L.Log("MM ServerHandler | Destroying self in client environment.");
            Destroy(this.gameObject);
        }
        else
        {
            IDictionary envs = Environment.GetEnvironmentVariables();
            DeploymentEnvs = new DeploymentEnvironmentDTO(envs);
            MatchEnvs = new MatchEnvironmentDTO<MyTicketsAttributes>(envs);
            
            BackfillAssignment deployment = new BackfillAssignment()
            {
                RequestID = DeploymentEnvs.RequestID,
                Fqdn = DeploymentEnvs.Fqdn,
                PublicIP = DeploymentEnvs.PublicIP,
                Ports = DeploymentEnvs.PortMapping,
                Location = DeploymentEnvs.Location,
            };
            BackfillAttributes = new BackfillAttributes(deployment);

            Request = new SafeHttpRequest(this);

            MatchmakingServer = new ServerAgent<
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
                MatchEnvs.Tickets,
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
                // handle backfills
                (
                    Observable<Dictionary<string, BackfillResponseDTO<MyTicketsAttributes>>> backfills,
                    ObservableActionType action,
                    string message
                ) =>
                {
                    if (
                        action == ObservableActionType.Update
                        && (message.Contains("assigned") || message.Contains("abandon"))
                    )
                    {
                        // todo handling
                    }

                    if (message.Contains("create"))
                    {
                        --UnprocessedBackfills;
                    }
                }
            );

            // todo listen for leaving players & their ticketID => MatchmakingServer.RemoveAssignment(ticketID);
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (BackfillRunning && MatchmakingServer.Assignments.Count == 0)
        {
            StopBackfill(() =>
            {
                StopServer();
            });
        }

        if (
            BackfillRunning
            && TargetPlayerCount > 0
            && MatchmakingServer.Assignments.Count
                + MatchmakingServer.Backfills.Current.Count
                + UnprocessedBackfills
                < TargetPlayerCount
        )
        {
            StartNewBackfill();
        }
    }

    public void OnApplicationQuit()
    {
        if (!DeleteBackfillOnQuit)
            return;
        StopBackfill();
    }

    public void StartNewBackfill()
    {
        ++UnprocessedBackfills;

        MyBackfillRequestDTO backfill = new MyBackfillRequestDTO(
            MatchEnvs.MatchProfile,
            BackfillAttributes,
            MatchmakingServer.Assignments
        );

        MatchmakingServer.AddBackfill(backfill);
    }

    public void StopBackfill(Action onCompletedDelegate = null)
    {
        BackfillRunning = false;
        MatchmakingServer.RemoveAllBackfills(onCompletedDelegate);
    }

    public void StopServer()
    {
        Request.Delete(
            DeploymentEnvs.SelfStopURL,
            DeploymentEnvs.SelfStopToken,
            (string response, UnityWebRequest request) =>
            {
                L.Log($"MM ServerHandler | Successfully called Self-Stop API.\n{response}");
            },
            (string error, UnityWebRequest request) =>
            {
                L.Error($"MM ServerHandler | Couldn't reach Self-Stop API.\n{error}");
            },
            new RetryParameters { MaxAttempts = 10 }
        );
    }
}
