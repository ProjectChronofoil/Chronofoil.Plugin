# Chronofoil.Plugin

This is the plugin for Chronofoil that runs inside FFXIV and captures packets.
The API portion is [here](https://github.com/ProjectChronofoil/Chronofoil.Web).

## What is it?

Chronofoil captures packets and optionally allows you to upload these packets to the central Chronofoil server.
There is more information in the plugin itself, but the overall intent of all aspects of Chronofoil is preservation
of FFXIV. Preservation efforts for singleplayer games often consist of keeping copies of each version, because anyone
with that version can go back and experience how it played at that time. For online games, this is not possible, which
is why capturing all traffic during normal gameplay is necessary to fully preserve the game. Crowdsourcing these
captures results in a comprehensive archive of how the game behaved during a specific game version. That is the
goal of Chronofoil.

## Isn't there sensitive stuff in those packets?

Yes. The Chronofoil plugin records all TCP-based game traffic (no HTTP yet) going to and from the game server. However,
when uploading, captures are censored based on known packets that contain sensitive information. Censoring is done by the plugin
before uploading takes place; this information never leaves your system.

Current censored fields are:
- The user's session ID, which is how the lobby knows how to log the client in as you
- All chat, including the chat protocol (FC, Linkshell, etc) as well as zone chat (shout, say, etc)
- All letters, including the letter list, opening individual letters, and sending letters in-game

## Can I opt-out?

You can opt to not use the plugin at all, or you can utilize the plugin, but not opt to upload any captures. Uploading
is disabled by default, and you cannot upload until you sign in with an external auth provider (currently only Discord is supported)
and accept the Terms of Service of Chronofoil Services. Uploading is manual and per-capture; there is no "automatic upload" of any kind.
You don't need to register in order to capture packets for your own purposes, nor are you beholden to any terms other than the license
of this software when using it for personal use.

The way Chronofoil is designed is to record all traffic. Therefore, it is not possible, at the design level, to remove certain
players from these collected packets. I am sorry.

While significantly different, this is no more invasive than say, someone recording every one of their game sessions and posting it 
online for all to see. Chronofoil does not upload chat or letter content, so communications inside the game stay private.

## I'm a developer! This is useful to me, how can I see what's in my captures?

The capture file format is implemented entirely in [this library](https://github.com/ProjectChronofoil/Chronofoil.CaptureFile). This way,
other applications can easily read and write capture files. I would suggest using this library to write frames to a file,
then using a hex editor with a template system to parse them. As mentioned above, you do not need to register, log in, or otherwise
upload any captures in order to use Chronofoil for personal research.

Apologies, better tools for exploring captures are in development. The focus was on getting Chronofoil live before June 28th, 2024.

## I used the "old version" of Chronofoil (compiled manually), can I upload those captures?

Not in their current state. They must be converted. This isn't a trivial process, especially if you want to maintain your context directories.
As mentioned above, tooling is in development for this as well.

## Installation
You can install Chronofoil by adding the following URL to the custom plugin repositories list in your Dalamud settings.

Follow these steps:

1. Type `/xlsettings` in-game
2. Go to the "Experimental" tab
3. Copy and paste the repo.json link below into one of the empty text boxes
4. Click on the + button next to the link you've just pasted
5. Click on the circular "Save and Close" button in the bottom-right corner
6. You will now see Chronofoil listed in the Available Plugins tab in the Dalamud Plugin Installer
7. Do not forget to actually install Chronofoil from this tab once ready

https://raw.githubusercontent.com/ProjectChronofoil/Chronofoil.Plugin/main/repo.json