using System;
using System.Text;
using SmolNetSharp.Protocols;

namespace GeminiConsole
{
    class Program
    {
        static string InviteInput() {
            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            Console.WriteLine("GeminiConsole app. Type exit to quit. ");
            Console.WriteLine("~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~");
            Console.Write("Enter the Gemini or Gopher URL: ");
            var userText = Console.ReadLine();
            return (userText);
        }

        static void Main(string[] args)
        {
            Uri uri;

            if (args.Length == 0)
            {
                var userText = InviteInput();

                //basic interaction loop
                while (userText != "exit" )
                {
                    try
                    {
                        uri = new Uri(userText);
                        Navigate(uri);
                    } catch (Exception err)
                    {
                        ReportIt("Error: " + err.Message);
                    }

                    userText = InviteInput();   //ask again
               }
            }
            else
            {
                uri = new Uri(args[0]);
                Navigate(uri);
            }
        }


        private static void ReportIt(string msg)
        {
            Console.WriteLine(msg);
        }

        private static  bool Navigate(Uri target)
        {
            IResponse resp;

            try
            {
                switch (target.Scheme)
                {
                    case "gemini":
                        resp = Gemini.Fetch(target);
                        break;
                    case "gopher":
                        resp = Gopher.Fetch(target);
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

            switch (resp.mime)
            {
                case "text/gemini":
                case "application/gopher-menu":
                case "text/plain":
                    {
                        if (target.Scheme == "gemini")
                        {
                            var geminiResp = (GeminiResponse)resp;
                            ReportIt(geminiResp.meta);
                        }

                        string input = Encoding.UTF8.GetString(resp.pyld.ToArray());

                        ReportIt(input);
                        break;
                    }

                default: // report the mime type only for now
                    ReportIt("Some " + resp.mime + " content was received");
                    break;
            }

            return true;
        }
    }
}
