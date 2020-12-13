using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

using SmolNetSharp.Protocols;

namespace SmolNetSharp.Test
{
    [TestClass]
    public class UnitTests
    {

        //some test urls - convert into test cases

        //uri = new Uri("gemini://gemini.circumlunar.space");
        //uri = new Uri("gopher://gopher.floodgap.com");
        //uri = new Uri("gopher://gopher.floodgap.com/0/gopher/wbgopher");
        //uri = new Uri("gopher://gopherpedia.com/0/Alpine%20newt");       //it seems %20s need to be converted back to spaces when talking to the server
        //uri = new Uri("gopher://gopherpedia.com");       //this works

        //uri = new Uri("gopher://gopher.conman.org/1phlog.gopher");
        //uri = new Uri("gopher://gopher.conman.org/IPhlog:2020/11/03/vote-for-biden.jpg");
        //uri = new Uri("gemini://gemini.djinn.party/");
        //uri = new Uri("gemini://calcuode.com/gemlog/index.gmi");
        //uri = new Uri("gemini://gemini.circumlunar.space/docs/specification.gmi");

        

        [TestMethod]
        public void TestGemini()
        {

            var uri = new Uri("gemini://gemini.circumlunar.space/docs/specification.gmi");
           
            GeminiResponse resp = (GeminiResponse)Gemini.Fetch(uri);

            Assert.AreEqual( '2', resp.codeMajor);
            Assert.IsTrue(resp.mime.StartsWith("text/gemini"));


        }

        [TestMethod]
        public void TestImage()
        {
            GeminiResponse resp = (GeminiResponse)Gemini.Fetch(
                new Uri("gemini://gemini.marmaladefoo.com/geminaut/gus_home.png")
            );
            Assert.AreEqual("image/png", resp.mime);

        }

        [TestMethod]
        public void TestGopher()
        {

            var uri = new Uri("gopher://gopher.floodgap.com");

            IResponse resp = Gopher.Fetch(uri);

            Assert.IsTrue(resp.mime.StartsWith("application/gopher-menu"));


        }
    }
}
