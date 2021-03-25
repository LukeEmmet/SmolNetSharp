using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.CommandLineUtils;
using SmolNetSharp.Protocols;

namespace GeminiConsole
{
    class Program
    {
            static string InviteInput() {

            Console.Write("\n\n");
            Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            Console.WriteLine("GeminiConsole app. Type exit to quit. ");
            Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            Console.Write("Enter the Gemini, Nimigem or Gopher URL: ");
            var userText = Console.ReadLine();
            return (userText);
        }

        private static void Greet(string greeting)
        {
            Console.WriteLine(greeting);
        }

        static void Main(string[] args)
        {
            Uri uri;

            //can convert pem to pfx online if you want or use some other tool to create them
            //e.g. https://cservices.certum.pl/muc-customer/pfx/generator

            X509Certificate2 clientCertificate = null;
            bool insecureFlag = false;

            CommandLineApplication commandLineApplication =   new CommandLineApplication(throwOnUnexpectedArg: false);

            CommandOption cert = commandLineApplication.Option(
              "-c | --cert <path>", "path to pfx certificate",
              CommandOptionType.SingleValue);

            CommandOption pass = commandLineApplication.Option(
              "-p | --password <password>", "password for pfx certificate",
              CommandOptionType.SingleValue);

            CommandOption insecure = commandLineApplication.Option(
              "-i | --insecure", "connect without checking server cert",
              CommandOptionType.NoValue);

            commandLineApplication.HelpOption("-? | -h | --help");

            commandLineApplication.OnExecute(() =>
            {
                Console.WriteLine(cert.Value());
                
                if (cert.HasValue())
                {
                    string pfxPath = cert.Value();
                    string pfxPass = pass.Value();

                    Console.Write("Loading certificate: " + pfxPath + "...");
                    clientCertificate = new X509Certificate2(pfxPath, pfxPass);
                    Console.WriteLine("done.");
                }

                insecureFlag = insecure.HasValue();
                if (insecureFlag)
                {
                    Console.WriteLine("Insecure mode - no checking of server certs");
                }

                var userText = InviteInput();
                var payload = "";

                //basic interaction loop
                while (userText != "exit")
                {

                    try
                    {
                        uri = new Uri(userText);
                        if (uri.Scheme == "nimigem")
                        {
                            Console.Write("Nimigem payload (as plain text): ");
                            var payloadRaw =  Console.ReadLine();
                            payload = payloadRaw;

                            if (payloadRaw.Length > 0)
                            {
                                //encode the payload - for ease of user debugging (nimigem client actually does it as well)
                                //var payloadEncoder = new UriBuilder();
                                //payloadEncoder.Path = payloadRaw;
                                Console.WriteLine("\nPayload: " + payloadRaw);
                            }
                            Console.WriteLine("");

                        }
                        Navigate(uri, clientCertificate, insecureFlag, payload);
                    }
                    catch (Exception err)
                    {
                        ReportIt("Error: " + err.Message);
                    }

                    userText = InviteInput();   //ask again
                    payload = "";
               }

                return 0;
            });
            commandLineApplication.Execute(args);

        }


        private static void ReportIt(string msg)
        {
            Console.WriteLine(msg);
        }

        private static bool Navigate(Uri target, X509Certificate2 cert, bool insecure, string payload)
        {
            IResponse resp;

            try
            {
                switch (target.Scheme)
                {
                    case "gemini":
                        resp = Gemini.Fetch(target, cert, "", insecure);
                        break;
                    case "gopher":
                        resp = Gopher.Fetch(target);
                        break;
                    case "nimigem":
                        resp = Nimigem.Fetch(target, Encoding.UTF8.GetBytes(payload), "text/plain", cert, "", insecure);
                        break;
                    default:
                        ReportIt(string.Format("Unknown URI scheme '{0}'", target.Scheme));
                        return false;
                }
            }
            catch (Exception e)
            {
                ReportIt("Error loading page: " + e.Message);
                return false;
            }

            //show the meta for gemini and nimigem
            if (target.Scheme == "gemini")
            {
                var geminiResp = (GeminiResponse)resp;

                ReportIt("Gemini response");

                if (resp.uri.AbsoluteUri != target.AbsoluteUri)
                {
                    //was redirected, let the user know
                    ReportIt("\tredirected to: " + resp.uri.AbsoluteUri);
                }

                ReportIt("\theader: " + geminiResp.codeMajor.ToString() + geminiResp.codeMinor.ToString() + " " + geminiResp.meta);



            }
            else if (target.Scheme == "nimigem")
            {
                var nimigemResp = (NimigemResponse)resp;

                ReportIt("Nimigem response");

                if (resp.uri.AbsoluteUri != target.AbsoluteUri)
                {
                    //was redirected, let the user know
                    ReportIt("\tredirected to: " + resp.uri.AbsoluteUri);
                }

                ReportIt("\theader: " + nimigemResp.codeMajor.ToString() + nimigemResp.codeMinor.ToString() + " " + nimigemResp.meta);

                switch (nimigemResp.codeMajor)
                {
                    case '1':

                        //behaviour unspecified at present - probably invalid
                        ReportIt(String.Format("\tInvalid Nimigem 1X response code {0}{1}", nimigemResp.codeMajor, nimigemResp.codeMinor));
                        break;

                    case '2':
                        switch (nimigemResp.codeMinor)
                        {
                            case '5':
                                ReportIt("\tNimigem success, content should be retrieved from: " + nimigemResp.meta);
                                break;

                            default:
                                //should be raised as an error from smolnetsharp, but handle it anyway
                                ReportIt(String.Format("\tInvalid Nimigem 2X success code {0}{1}: No other success responses are permitted than 25",
                                    nimigemResp.codeMajor, nimigemResp.codeMinor));
                                break;
                        }
                        break;

                    //case 3 is a redirect and is handled by smolnetsharp up to a standard number of redirects

                    //4, 5 and 6  are error responses, should be indicated in meta
                    case '4':
                    case '5':
                    case '6':
                        ReportIt(String.Format("\tServer error response: {0}", nimigemResp.meta));
                        break;

                    default:
                        //others are invalid
                        ReportIt(String.Format("\tInvalid Nimigem {0}X response code {0}{1}\nServer response: {0}{1} {2}",
                            nimigemResp.codeMajor, nimigemResp.codeMinor));
                        break;

                }

                return true;
            }

            //report the body for gemini and gopher only (nimigem has no response body)
            if (target.Scheme == "gemini" || target.Scheme == "gopher")
            {
                switch (resp.mime)
                {
                    case "text/gemini":
                    case "application/gopher-menu":
                    case "text/plain":
                        {
                            string body = Encoding.UTF8.GetString(resp.bytes.ToArray());

                            ReportIt(body);
                            break;
                        }

                    default: // report the mime type only for now
                        ReportIt("Some " + resp.mime + " content was received");
                        break;
                }
            }

            return true;
        }
    }
}
