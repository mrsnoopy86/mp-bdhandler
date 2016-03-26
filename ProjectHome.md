Bluray Handler is a simple process plugin for the MediaPortal media center application. The plugin integrates into the internal player factory and presents users with a playlist selection dialog when bluray media is detected.

**Features**

  * Bluray playback using the internal player (.mpls / supports seamless branching )
  * Feature selection menu (not actual BD menu)
  * Auto-play support (using same settings as DVD autoplay)
  * Chapter support (ability to skip next/prev)
  * Improved audio en subtitle handling (.mpls / .m2ts)

**Requirements**

  * Latest "MPC - Mpeg Source (Gabest)" (mpegsplitter.ax) registered on your system
  * AnyDVD HD (or similar tool) for playing protected discs.

**Installation**

Unzip the archive and copy the DLL into the "Mediaportal/plugins/proces" folder.
Make sure the plugin is enabled in your configuration (default should be enabled)

**Usage**

Because this plugin is currently in beta i would love to have some users test a range of bluray media for me. You can test it by adding ".bdmv" as an extension in mediaportal. From within the My Videos you can now browse to your bluray disk and select the "index.bdmv" to playback the disc. Inserting a bluray disc will trigger autoplay when you have this setup for DVD. The "Play Disc" button will also function for bluray now.