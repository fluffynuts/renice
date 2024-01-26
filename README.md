# renice
A windows-only utility to alter process priority with a few extras. Made by a spider-monkey on uppers, the code isn't pretty, but it wurks grate!

## Get it
[on the releases page](https://github.com/fluffynuts/renice/releases) or roll your own from the source here (:

If you download from the releases page, be sure to put the binary in a folder that's in your PATH, or add the folder you're keeping it in to your path. If you build yourself via the npm script, you'll get `%USERPROFILE%\.local\bin\renice.exe`

## Roll your own
You'll need a few bits and pieces. Bare minimum is dotnet sdk version 8. If you'd like to be able to build without knowing anything about the build process, also install node (at least version 16, or select v16+ with your node version manager - I recommend [nvs](https://github.com/jasongin/nvs)

1. `npm i`
2. `npm run publish-local`
3. add the folder `.local\bin` from your profile directory to the path (typically, something like `C:\users\your.user.name\.local\bin`) - this is where the local `renice.exe` was placed (or go find it under the publish folder in the project, if you like)