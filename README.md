# SmolNetSharp

.NET C# client library for the small net - Gemini and Gopher

Smolnet - the small net where life is calmer and smoother. 

Gemini and Gopher are small net protocols for sharing content on the Internet.

SmolNetSharp is a .NET C# client library you can use to build your own Gemini or Gopher clients in .NET.

# Usage

Include the SmolNet project or compiled library in your .NET solution.

There are two classes Gemini.cs and Gopher.cs which let you fetch the content. They return the content as a byte array together with the MIME type.

# Features

* Client code for Gemini and Gopher, using a common interface
* Ability to set a timeout to abandon long downloads
* Ability to set a max size to abandon large downloads
* Converts Gopher item types to mime types
* Support for Gemini proxies
* Support for the Nimigem protocol - a complementary content submission protocol to work with Gemini (experimental)
* Compiles on Windows and Linux too (since .NET Core)

Gophermaps are returned with type application/gopher-menu.

# Demo app

There is a very simplistic test console app in the GeminiConsole folder

# Acknowledgements

The Gemini client class was initially extracted from TwinPeaks by InvisibleUp, but has been improved since. Also some ideas from BoringCactus Gemini client

* https://github.com/InvisibleUp/twinpeaks
* https://git.sr.ht/~boringcactus/dotnet-gemini