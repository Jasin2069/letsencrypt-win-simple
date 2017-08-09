﻿using ACMESharp;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography.X509Certificates;

namespace LetsEncrypt.ACME.Simple
{
    public class WebDavPlugin : Plugin
    {
        private NetworkCredential WebDavCredentials { get; set; }

        public override string Name => "WebDav";

        public override List<Target> GetTargets()
        {
            var result = new List<Target>();

            return result;
        }

        public override List<Target> GetSites()
        {
            var result = new List<Target>();

            return result;
        }

        public override void Install(Target target, string pfxFilename, X509Store store, X509Certificate2 certificate)
        {
            if (!string.IsNullOrWhiteSpace(Program.Options.Script) &&
                !string.IsNullOrWhiteSpace(Program.Options.ScriptParameters))
            {
                var parameters = string.Format(Program.Options.ScriptParameters, target.Host,
                    Properties.Settings.Default.PFXPassword,
                    pfxFilename, store.Name, certificate.FriendlyName, certificate.Thumbprint);
                Log.Information("Running {Script} with {parameters}", Program.Options.Script, parameters);
                Process.Start(Program.Options.Script, parameters);
            }
            else if (!string.IsNullOrWhiteSpace(Program.Options.Script))
            {
                Log.Information("Running {Script}", Program.Options.Script);
                Process.Start(Program.Options.Script);
            }
            else
            {
                Log.Warning("Unable to configure server software.");
            }
        }

        public override void Install(Target target)
        {
            // This method with just the Target paramater is currently only used by Centralized SSL
            if (!string.IsNullOrWhiteSpace(Program.Options.Script) &&
                !string.IsNullOrWhiteSpace(Program.Options.ScriptParameters))
            {
                var parameters = string.Format(Program.Options.ScriptParameters, target.Host,
                    Properties.Settings.Default.PFXPassword, Program.Options.CentralSslStore);
                Log.Information("Running {Script} with {parameters}", Program.Options.Script, parameters);
                Process.Start(Program.Options.Script, parameters);
            }
            else if (!string.IsNullOrWhiteSpace(Program.Options.Script))
            {
                Log.Information("Running {Script}", Program.Options.Script);
                Process.Start(Program.Options.Script);
            }
            else
            {
                Log.Warning("Unable to configure server software.");
            }
        }

        public override void Renew(Target target)
        {
            Log.Warning("Renewal is not supported for the Web Dav Plugin.");
        }

        public override void PrintMenu()
        {
            Console.WriteLine(" W: Generate a certificate via WebDav and install it manually.");
        }

        public override void HandleMenuResponse(string response, List<Target> targets)
        {
            if (response == "w")
            {
                var hostName = Input.RequestString("Enter a host name");

                string[] alternativeNames = null;

                if (Program.Options.San)
                {
                    Console.Write(" Enter all Alternative Names seperated by a comma: ");
                    Console.SetIn(new System.IO.StreamReader(Console.OpenStandardInput(8192)));
                    var sanInput = Console.ReadLine();
                    alternativeNames = sanInput.Split(',');
                }
                Console.WriteLine(" Enter a site path (the web root of the host for http authentication)");
                Console.WriteLine(" Example, http://domain.com:80/");
                Console.WriteLine(" Example, https://domain.com:443/");
                Console.Write(": ");
                var webDavPath = Console.ReadLine();

                var webDavUser = Input.RequestString("Enter the WebDAV username");
                var webDavPass = Input.ReadPassword("Enter the WebDAV password");

                WebDavCredentials = new NetworkCredential(webDavUser, webDavPass);

                List<string> sanList = new List<string>();

                if (alternativeNames != null)
                {
                    sanList = new List<string>(alternativeNames);
                }
                if (sanList.Count <= Settings.maxNames)
                {
                    var target = new Target()
                    {
                        Host = hostName,
                        WebRootPath = webDavPath,
                        PluginName = Name,
                        AlternativeNames = sanList
                    };
                    Auto(target);
                }
                else
                {
                    Log.Error($"You entered too many hosts for a San certificate. Let's Encrypt currently has a maximum of {Settings.maxNames} alternative names per certificate.");
                }
            }
        }

        public override void Auto(Target target)
        {
            if (WebDavCredentials != null)
            {
                var auth = Program.Authorize(target);
                if (auth.Status == "valid")
                {
                    var pfxFilename = Program.GetCertificate(target);
                    Log.Information("You can find the certificate at {pfxFilename}", pfxFilename);
                }
            }
            else
            {
                Log.Error("The Web Dav Credentials are not set. Please specify them and try again.");
            }
        }

        public override void CreateAuthorizationFile(string answerPath, string fileContents)
        {
            Log.Debug("Writing challenge answer to {answerPath}", answerPath);
            Upload(answerPath, fileContents);
        }

        private void Upload(string webDavPath, string content)
        {
            Uri webDavUri = new Uri(webDavPath);
            Log.Debug("webDavUri {@webDavUri}", webDavUri);
            var scheme = webDavUri.Scheme;
            string webDavConnection = scheme + "://" + webDavUri.Host + ":" + webDavUri.Port;
            int pathLastSlash = webDavUri.AbsolutePath.LastIndexOf("/") + 1;
            string file = webDavUri.AbsolutePath.Substring(pathLastSlash);
            string path = webDavUri.AbsolutePath.Remove(pathLastSlash);
            Log.Debug("webDavConnection {@webDavConnection}", webDavConnection);

            Log.Debug("UserName {@UserName}", WebDavCredentials.UserName);

            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(content);
            writer.Flush();
            stream.Position = 0;

            Log.Debug("stream {@stream}", stream);

            var client = new WebDAVClient.Client(WebDavCredentials);
            client.Server = webDavConnection;
            client.BasePath = path;

            var fileUploaded = client.Upload("/", stream, file).Result;

            Log.Information("Upload Status {StatusDescription}", fileUploaded);
        }

        private async void Delete(string webDavPath)
        {
            Uri webDavUri = new Uri(webDavPath);
            Log.Debug("webDavUri {@webDavUri}", webDavUri);
            var scheme = webDavUri.Scheme;
            string webDavConnection = scheme + "://" + webDavUri.Host + ":" + webDavUri.Port;
            string path = webDavUri.AbsolutePath;
            Log.Debug("webDavConnection {@webDavConnection}", webDavConnection);

            Log.Debug("UserName {@UserName}", WebDavCredentials.UserName);

            var client = new WebDAVClient.Client(WebDavCredentials);
            client.Server = webDavConnection;
            client.BasePath = path;

            try
            {
                await client.DeleteFile(path);
            }
            catch (Exception ex)
            {
                Log.Warning("Error deleting file/folder {@ex}", ex);
            }

            string result = "N/A";

            Log.Information("Delete Status {StatusDescription}", result);
        }

        private string GetFiles(string webDavPath)
        {
            Uri webDavUri = new Uri(webDavPath);
            Log.Debug("webDavUri {@webDavUri}", webDavUri);
            var scheme = webDavUri.Scheme;
            string webDavConnection = scheme + "://" + webDavUri.Host + ":" + webDavUri.Port;
            string path = webDavUri.AbsolutePath;
            Log.Debug("webDavConnection {@webDavConnection}", webDavConnection);

            Log.Debug("UserName {@UserName}", WebDavCredentials.UserName);

            var client = new WebDAVClient.Client(WebDavCredentials);
            client.Server = webDavConnection;
            client.BasePath = path;

            var folderFiles = client.List().Result;
            string names = "";
            foreach (var file in folderFiles)
            {
                names = names + file.DisplayName + ",";
            }

            Log.Debug("Files {@names}", names);
            return names.TrimEnd('\r', '\n', ',');
        }

        private readonly string _sourceFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web_config.xml");

        public override void BeforeAuthorize(Target target, string answerPath, string token)
        {
            answerPath = answerPath.Remove((answerPath.Length - token.Length), token.Length);
            var webConfigPath = Path.Combine(answerPath, "web.config");

            Log.Debug("Writing web.config to add extensionless mime type to {webConfigPath}", webConfigPath);

            Upload(webConfigPath, File.ReadAllText(_sourceFilePath));
        }

        public override void DeleteAuthorization(string answerPath, string token, string webRootPath, string filePath)
        {
            Log.Debug("Deleting answer");
            Delete(answerPath);

            try
            {
                if (Properties.Settings.Default.CleanupFolders == true)
                {
                    var folderPath = answerPath.Remove((answerPath.Length - token.Length), token.Length);
                    var files = GetFiles(folderPath);

                    if (!string.IsNullOrWhiteSpace(files))
                    {
                        if (files == "web.config")
                        {
                            Log.Debug("Deleting web.config");
                            Delete(folderPath + "web.config");
                            Log.Debug("Deleting {folderPath}", folderPath);
                            Delete(folderPath);
                            var filePathFirstDirectory =
                                Environment.ExpandEnvironmentVariables(Path.Combine(webRootPath,
                                    filePath.Remove(filePath.IndexOf("/"), (filePath.Length - filePath.IndexOf("/")))));
                            Log.Debug("Deleting {filePathFirstDirectory}", filePathFirstDirectory);
                            Delete(filePathFirstDirectory);
                        }
                        else
                        {
                            Log.Warning("Additional files exist in {folderPath} not deleting.", folderPath);
                        }
                    }
                    else
                    {
                        Log.Warning("Additional files exist in {folderPath} not deleting.", folderPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Error occured while deleting folder structure. Error: {@ex}", ex);
            }
        }
    }
}