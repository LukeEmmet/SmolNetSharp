using Serilog;
using System;
using System.Text;
using System.Threading.Tasks;
using SmolNetSharp.Protocols;

namespace GeminiConsole
{
    class Program
    {
        static void Main(string[] args)
        {

            Uri uri;

            if (args.Length == 0)
            {

                //some test urls

                //uri = new Uri("gemini://gemini.circumlunar.space");
                //uri = new Uri("gopher://gopher.floodgap.com");
                //uri = new Uri("gopher://gopher.floodgap.com/0/gopher/wbgopher");
                //uri = new Uri("gopher://gopherpedia.com/0/Alpine%20newt");       //it seems %20s need to be converted back to spaces when talking to the server
                //uri = new Uri("gopher://gopherpedia.com");       //this works

                //uri = new Uri("gopher://gopher.conman.org/1phlog.gopher");
                //uri = new Uri("gopher://gopher.conman.org/IPhlog:2020/11/03/vote-for-biden.jpg");
                //uri = new Uri("gemini://gemini.djinn.party/");
                //uri = new Uri("gemini://calcuode.com/gemlog/index.gmi");
                uri = new Uri("gemini://gemini.circumlunar.space/docs/specification.gmi");

                Console.WriteLine("No url was passed - showing a default uri: " + uri.ToString());
            }
            else
            {
                uri = new Uri(args[0]);
            }

            var result =  Navigate(uri);
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
                        Log.Error("Unknown URI scheme '{scheme}'", target.Scheme);
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
