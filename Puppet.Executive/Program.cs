﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Puppet.Common.Automation;
using Puppet.Common.Configuration;
using Puppet.Common.Events;
using Puppet.Common.Services;
using Puppet.Executive.Automation;
using Puppet.Executive.Mqtt;

namespace Puppet.Executive
{
    class Program
    {
        const string APPSETTINGS_FILENAME = "appsettings.json";

        static AutomationTaskManager _taskManager;
        static HomeAutomationPlatform _hub;
        static IMqttService _mqtt;

        public static async Task Main(string[] args)
        {
            // Read the configuration file
            IConfiguration configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory()) // Directory where the json files are located
                .AddJsonFile(APPSETTINGS_FILENAME, optional: false, reloadOnChange: true)
                .Build();

            // Create an HttpClient that doesn't validate the server certificate
            HttpClientHandler customHttpClientHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => { return true; }
            };
            HttpClient _httpClient = new HttpClient(customHttpClientHandler);

            // Abstraction representing the home automation system
            _hub = new Hubitat(configuration, _httpClient);

            // Start the MQTT service, if applicable.
            MqttOptions mqttOptions = configuration.GetSection("MQTT").Get<MqttOptions>();
            if (mqttOptions?.Enabled ?? false)
            {
                _mqtt = new MqttService(await MqttClientFactory.GetClient(mqttOptions),
                                        mqttOptions,
                                        _hub);
                await _mqtt.Start();
            }

            // Class to manage long-running tasks
            _taskManager = new AutomationTaskManager();

            // Bind a method to handle the events raised
            // by the Hubitat device
            _hub.AutomationEvent += Hub_AutomationEvent;
            var hubTask = _hub.StartAutomationEventWatcher();

            // Wait forever, this is a daemon process
            await hubTask;
        }



        /// <summary>
        /// Event handler for AutomationEvents raised by the HomeAutomationPlatform.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private static void Hub_AutomationEvent(object sender, Common.Events.AutomationEventEventArgs e)
        {
            var evt = e.HubEvent;

            Task.Run(() => StartRelevantAutomationHandlers(evt));
            Task.Run(() => SendEventToMqtt(evt));
            //Task.Run(() => SendEventToAlexa(evt));
        }

        private static void SendEventToAlexa(HubEvent evt)
        {
            // TODO: Forward events to Alexa
        }

        private static void SendEventToMqtt(HubEvent evt)
        {
            _mqtt?.SendEventToMqttAsync(evt);
        }

        private static void StartRelevantAutomationHandlers(HubEvent evt)
        {
            // Get a reference to the automation
            var automations = AutomationFactory.GetAutomations(evt, _hub);

            foreach (IAutomation automation in automations)
            {
                // If this automation is already running, cancel all running instances
                _taskManager.CancelRunningInstances(automation.GetType(), evt.DeviceId);

                // Start a task to handle the automation and a CancellationToken Source
                // so we can cancel it later.
                var cts = new CancellationTokenSource();
                Func<Task> handleTask = async () =>
                {
                    var startedTime = DateTime.Now;
                    Console.WriteLine($"{DateTime.Now} {automation} event: {evt.DescriptionText}");
                    try
                    {
                        // This runs the Handle method on the automation class
                        await automation.Handle(cts.Token);
                    }
                    catch (TaskCanceledException)
                    {
                        Console.WriteLine($"{DateTime.Now} {automation} event from {startedTime} cancelled.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{DateTime.Now} {automation} {ex} {ex.Message}");
                    }
                };

                // Ready... go handle it!
                Task work = Task.Run(handleTask, cts.Token);

                // Hold on to the task and its cancellation token source for later.
                _taskManager.Track(work, cts, automation.GetType(), evt.DeviceId);
            }

            // Let's take this opportunity to get rid of any completed tasks.
            _taskManager.RemoveCompletedTasks();
        }
    }
}
