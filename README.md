# SmolNetSharp

.NET C# client library for the small net - Gemini and Gopher

Smolnet - the small net where life is calmer and smoother. 

Gemini and Gopher are small net protocols for sharing content on the Internet.

SmolNetSharp is a .NET C# client library you can use to build your own Gemini or Gopher clients in .NET.

# Usage

Include the SmolNet project or compiled library in your .NET solution.

There are two classes Gemini.cs and Gopher.cs which let you fetch the content. They return the content as a byte array together with the MIME type.

# Demo app

There is a very simplistic test console app in the GeminiConsole folder

# Acknowledgements

The Gemini client class was extracted from TwinPeaks by InvisibleUp, although it has been updated and augmented since.

* see https://github.com/InvisibleUp/twinpeaks