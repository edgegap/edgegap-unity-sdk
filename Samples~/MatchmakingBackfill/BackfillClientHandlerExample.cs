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
                //MTODO
            },
            // handle group assignment
            (
                Observable<GroupUpResponseDTO> group,
                ObservableActionType action,
                string message
            ) =>
            { 
                //MTODO
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

    public void StartMatchmaking()
    {
        //MTODO
    }

    public void StopMatchmaking()
    {
        //MTODO
    }
}
