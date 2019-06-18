// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Azure.IIoT.OpcUa.Api.Vault;
using Microsoft.Azure.IIoT.OpcUa.Api.Vault.v1;
using Mono.Options;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server.Database.OpcVault;
using Opc.Ua.Gds.Server.OpcVault;
using Opc.Ua.Server;

namespace Opc.Ua.Gds.Server {
    public class ApplicationMessageDlg : IApplicationMessageDlg {
        private string _message = string.Empty;
        private bool _ask;

        public override void Message(string text, bool ask) {
            _message = text;
            _ask = ask;
        }

        public override async Task<bool> ShowAsync() {
            if (_ask) {
                _message += " (y/n, default y): ";
                Console.Write(_message);
            }
            else {
                Console.WriteLine(_message);
            }
            if (_ask) {
                try {
                    var result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r'));
                }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
                catch
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true);
        }
    }

    public enum ExitCode {
        Ok = 0,
        ErrorServerNotStarted = 0x80,
        ErrorServerRunning = 0x81,
        ErrorServerException = 0x82,
        ErrorInvalidCommandLine = 0x100
    };

    public class Program {

        public static readonly string Name = "Azure Industrial IoT OPC UA Global Discovery Server";

        public static int Main(string[] args) {
            Console.WriteLine(Name);

            // command line options
            var showHelp = false;
            var opcVaultOptions = new OpcVaultApiOptions();
            var azureADOptions = new OpcVaultAzureADOptions();

            var options = new Mono.Options.OptionSet {
                { "v|vault=", "OpcVault Url", g => opcVaultOptions.BaseAddress = g },
                { "r|resource=", "OpcVault Resource Id", r => opcVaultOptions.ResourceId = r },
                { "c|clientid=", "AD Client Id", c => azureADOptions.ClientId = c },
                { "s|secret=", "AD Client Secret", s => azureADOptions.ClientSecret = s },
                { "a|authority=", "Authority", a => azureADOptions.Authority = a },
                { "t|tenantid=", "Tenant Id", t => azureADOptions.TenantId = t },
                { "h|help", "show this message and exit", h => showHelp = h != null },
            };

            try {
                IList<string> extraArgs = options.Parse(args);
                foreach (var extraArg in extraArgs) {
                    Console.WriteLine("Error: Unknown option: {0}", extraArg);
                    showHelp = true;
                }
            }
            catch (OptionException e) {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp) {
                Console.WriteLine("Usage: dotnet Microsoft.Azure.IIoT.Modules.OpcUa.Gds.dll [OPTIONS]");
                Console.WriteLine();

                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return (int)ExitCode.ErrorInvalidCommandLine;
            }

            var server = new VaultGlobalDiscoveryServer();
            server.Run(opcVaultOptions, azureADOptions);

            return (int)VaultGlobalDiscoveryServer.ExitCode;
        }
    }

    public class VaultGlobalDiscoveryServer {
        private OpcVaultGlobalDiscoveryServer _server;
        private Task _status;
        private DateTime _lastEventTime;

        public void Run(
            OpcVaultApiOptions opcVaultOptions,
            OpcVaultAzureADOptions azureADOptions) {

            try {
                ExitCode = ExitCode.ErrorServerNotStarted;
                ConsoleGlobalDiscoveryServer(opcVaultOptions, azureADOptions).Wait();
                Console.WriteLine("Server started. Press Ctrl-C to exit...");
                ExitCode = ExitCode.ErrorServerRunning;
            }
            catch (Exception ex) {
                Utils.Trace("ServiceResultException:" + ex.Message);
                Console.WriteLine("Exception: {0}", ex.Message);
                ExitCode = ExitCode.ErrorServerException;
                return;
            }

            var quitEvent = new ManualResetEvent(false);
            try {
                Console.CancelKeyPress += (sender, eArgs) => {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
#pragma warning disable RECS0022 // A catch clause that catches System.Exception and has an empty body
            catch
#pragma warning restore RECS0022 // A catch clause that catches System.Exception and has an empty body
            {
            }

            // wait for timeout or Ctrl-C
            quitEvent.WaitOne();

            if (_server != null) {
                Console.WriteLine("Server stopped. Waiting for exit...");

                using (var _server = this._server) {
                    // Stop status thread
                    this._server = null;
                    _status.Wait();
                    // Stop server and dispose
                    _server.Stop();
                }
            }

            ExitCode = ExitCode.Ok;
        }

        public static ExitCode ExitCode { get; private set; }

        private static void CertificateValidator_CertificateValidation(CertificateValidator validator, CertificateValidationEventArgs e) {
            if (e.Error.StatusCode == StatusCodes.BadCertificateUntrusted) {
                // GDS accepts any client certificate
                e.Accept = true;
                Console.WriteLine("Accepted Certificate: {0}", e.Certificate.Subject);
            }
        }

        private async Task ConsoleGlobalDiscoveryServer(
            OpcVaultApiOptions opcVaultOptions,
            OpcVaultAzureADOptions azureADOptions) {
            ApplicationInstance.MessageDlg = new ApplicationMessageDlg();
            var application = new ApplicationInstance {
                ApplicationName = Program.Name,
                ApplicationType = ApplicationType.Server,
                ConfigSectionName = "Microsoft.Azure.IIoT.Modules.OpcUa.Gds"
            };

            // load the application configuration.
            var config = await application.LoadApplicationConfiguration(false);

            // check the application certificate.
            var haveAppCertificate = await application.CheckApplicationInstanceCertificate(false, 0);
            if (!haveAppCertificate) {
                throw new Exception("Application instance certificate invalid!");
            }

            if (!config.SecurityConfiguration.AutoAcceptUntrustedCertificates) {
                config.CertificateValidator.CertificateValidation += new CertificateValidationEventHandler(CertificateValidator_CertificateValidation);
            }

            // get the DatabaseStorePath configuration parameter.
            var opcVaultConfiguration = config.ParseExtension<GlobalDiscoveryServerConfiguration>();

            // extract appId and vault name from database storage path
            var keyVaultConfig = opcVaultConfiguration.DatabaseStorePath?.Split(',');
            if (keyVaultConfig != null) {
                if (string.IsNullOrEmpty(opcVaultOptions.BaseAddress)) {
                    // try configuration using XML config
                    opcVaultOptions.BaseAddress = keyVaultConfig[0];
                }

                if (string.IsNullOrEmpty(opcVaultOptions.ResourceId)) {
                    if (keyVaultConfig.Length > 1 && !string.IsNullOrEmpty(keyVaultConfig[1])) {
                        opcVaultOptions.ResourceId = keyVaultConfig[1];
                    }
                }

                if (string.IsNullOrEmpty(azureADOptions.ClientId)) {
                    if (keyVaultConfig.Length > 2 && !string.IsNullOrEmpty(keyVaultConfig[2])) {
                        azureADOptions.ClientId = keyVaultConfig[2];
                    }
                }

                if (string.IsNullOrEmpty(azureADOptions.ClientSecret)) {
                    if (keyVaultConfig.Length > 3 && !string.IsNullOrEmpty(keyVaultConfig[3])) {
                        azureADOptions.ClientSecret = keyVaultConfig[3];
                    }
                }

                if (string.IsNullOrEmpty(azureADOptions.TenantId)) {
                    if (keyVaultConfig.Length > 4 && !string.IsNullOrEmpty(keyVaultConfig[4])) {
                        azureADOptions.TenantId = keyVaultConfig[4];
                    }
                }

                if (string.IsNullOrEmpty(azureADOptions.Authority)) {
                    if (keyVaultConfig.Length > 5 && !string.IsNullOrEmpty(keyVaultConfig[5])) {
                        azureADOptions.Authority = keyVaultConfig[5];
                    }
                }

            }

            var serviceClient = new OpcVaultLoginCredentials(opcVaultOptions, azureADOptions);
            var opcVaultServiceClient = VaultServiceApiEx.CreateClient(new Uri(opcVaultOptions.BaseAddress), serviceClient);
            var opcVaultHandler = new OpcVaultClientHandler(opcVaultServiceClient);

            // read configurations from OpcVault secret
            opcVaultConfiguration.CertificateGroups = await opcVaultHandler.GetCertificateConfigurationGroupsAsync(opcVaultConfiguration.BaseCertificateGroupStorePath);
            UpdateGDSConfigurationDocument(config.Extensions, opcVaultConfiguration);

            var certGroup = new OpcVaultCertificateGroup(opcVaultHandler);
            var requestDB = new OpcVaultCertificateRequest(opcVaultServiceClient);
            var appDB = new OpcVaultApplicationsDatabase(opcVaultServiceClient);

            requestDB.Initialize();
            // for UNITTEST set auto approve true
            _server = new OpcVaultGlobalDiscoveryServer(appDB, requestDB, certGroup, false);

            // start the server.
            await application.Start(_server);

            // print endpoint info
            var endpoints = application.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct();
            foreach (var endpoint in endpoints) {
                Console.WriteLine(endpoint);
            }

            // start the status thread
            _status = Task.Run(StatusThread);

            // print notification on session events
            _server.CurrentInstance.SessionManager.SessionActivated += EventStatus;
            _server.CurrentInstance.SessionManager.SessionClosing += EventStatus;
            _server.CurrentInstance.SessionManager.SessionCreated += EventStatus;

        }

        /// <summary>
        /// Updates the config extension with the new configuration information.
        /// </summary>
        private static void UpdateGDSConfigurationDocument(XmlElementCollection extensions, GlobalDiscoveryServerConfiguration gdsConfiguration) {
            var gdsDoc = new XmlDocument();
            var qualifiedName = EncodeableFactory.GetXmlName(typeof(GlobalDiscoveryServerConfiguration));
            var gdsSerializer = new XmlSerializer(typeof(GlobalDiscoveryServerConfiguration), qualifiedName.Namespace);
            using (var writer = gdsDoc.CreateNavigator().AppendChild()) {
                gdsSerializer.Serialize(writer, gdsConfiguration);
            }

            foreach (var extension in extensions) {
                if (extension.Name == qualifiedName.Name) {
                    extension.InnerXml = gdsDoc.DocumentElement.InnerXml;
                }
            }
        }


        private void EventStatus(Session session, SessionEventReason reason) {
            _lastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        private void PrintSessionStatus(Session session, string reason, bool lastContact = false) {
            lock (session.DiagnosticsLock) {
                var item = string.Format("{0,9}:{1,20}:", reason, session.SessionDiagnostics.SessionName);
                if (lastContact) {
                    item += string.Format("Last Event:{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
                }
                else {
                    if (session.Identity != null) {
                        item += string.Format(":{0,20}", session.Identity.DisplayName);
                    }
                    item += string.Format(":{0}", session.Id);
                }
                Console.WriteLine(item);
            }
        }

        private async Task StatusThread() {
            while (_server != null) {
                if (DateTime.UtcNow - _lastEventTime > TimeSpan.FromMilliseconds(6000)) {
                    var sessions = _server.CurrentInstance.SessionManager.GetSessions();
                    for (var ii = 0; ii < sessions.Count; ii++) {
                        var session = sessions[ii];
                        PrintSessionStatus(session, "-Status-", true);
                    }
                    _lastEventTime = DateTime.UtcNow;
                }
                await Task.Delay(1000);
            }
        }
    }
}
