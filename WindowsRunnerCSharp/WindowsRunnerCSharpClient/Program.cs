﻿using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using PlayFab.MultiplayerModels;
using PlayFab.QoS;

namespace WindowsRunnerCSharpClient
{
    /// <summary>
    ///   Simple executable that integrates with PlayFab's SDK.
    ///   It allocates a game server and makes an http request to that game server
    /// </summary>
    public class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        public static Task Main(string[] args)
        {
            RootCommand rootCommand = RootCommandConfiguration.GenerateCommand(Run);

            return rootCommand.InvokeAsync(args);
        }

        private static async Task Run(string titleId, string playerId, string buildId, string region)
        {
            PlayFabApiSettings settings = new PlayFabApiSettings() {TitleId = titleId};
            PlayFabClientInstanceAPI clientApi = new PlayFabClientInstanceAPI(settings);

            // Login
            var loginRequest = new LoginWithCustomIDRequest()
            {
                CustomId = playerId,
                CreateAccount = true
            };
            PlayFabResult<LoginResult> login = await clientApi.LoginWithCustomIDAsync(loginRequest);
            if (login.Error != null)
            {
                Console.WriteLine(login.Error.ErrorMessage);
                throw new Exception($"Login failed with HttpStatus={login.Error.HttpStatus}");
            }
            Console.WriteLine($"Logged in player {login.Result.PlayFabId} (CustomId={playerId})");

            // Measure QoS
            PlayFabSettings.staticSettings.TitleId = titleId;              // TODO this makes me sad
            await PlayFabClientAPI.LoginWithCustomIDAsync(loginRequest);   // TODO this makes me sad

            PlayFabQosApi qosApi = new PlayFabQosApi();
            QosResult qosResult = await qosApi.GetQosResultAsync(10000);
            if (qosResult.ErrorCode != 0)
            {
                Console.WriteLine(qosResult.ErrorMessage);
                throw new Exception($"QoS ping failed with ErrorCode={qosResult.ErrorCode}");
            }
            Console.WriteLine("Pinged QoS servers with results:");
            //string resultsStr = JsonSerializer.Serialize<List<QosRegionResult>>(qosResult.RegionResults); // TODO this doesn't work because they are fields
            string resultsStr = string.Join(Environment.NewLine,
                qosResult.RegionResults.Select(x => $"{x.Region} - {x.LatencyMs}ms"));
            Console.WriteLine(resultsStr);
            Console.WriteLine();
            
            // Allocate a server
            string sessionId = Guid.NewGuid().ToString();
            PlayFabMultiplayerInstanceAPI mpApi = new PlayFabMultiplayerInstanceAPI(settings, clientApi.authenticationContext);
            PlayFabResult<RequestMultiplayerServerResponse> server =
                await mpApi.RequestMultiplayerServerAsync(new RequestMultiplayerServerRequest()
                    {
                        BuildId = buildId,
                        PreferredRegions = qosResult.RegionResults.Select(x => x.Region).ToList(),
                        SessionId = sessionId
                    }
                );
            if (server.Error != null)
            {
                Console.WriteLine(server.Error.ErrorMessage);
                throw new Exception($"Allocation failed with HttpStatus={server.Error.HttpStatus}");
            }

            string serverLoc = $"{server.Result.IPV4Address}:{server.Result.Ports[0].Num}";
            Console.WriteLine($"Allocated server {serverLoc}");

            // Issue Http request against the server
            using (HttpResponseMessage getResult = await _httpClient.GetAsync("http://" + serverLoc))
            {
                getResult.EnsureSuccessStatusCode();
                
                Console.WriteLine("Received response:");
                string responseStr = await getResult.Content.ReadAsStringAsync();
                
                Console.WriteLine(responseStr);
                Console.WriteLine();
            }
        }
    }
}