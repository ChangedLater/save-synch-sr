I'm playing the game star rapture. I want to build a windows application that synchronises the save file for a group of friends so that anyone can host that same save file.

it should:
 * use git as the backing store for remote file storage
 * use LibGit2Sharp instead of git cli
 * auto discover the location of the save files
 * prompt the user if the local copy is not synchronised
 * have an input for "username" and git api key when opening the app and store these locally
 * use WPF
 * use git commit messages to track which user (from user name above) created this version of the save
 * backup local saves before download the remote (this does not need to be in git)
 * save files can only be snychronised if the current user has created a local save file of the same name (session name in game ui)
   * the save file synchronisation should be managed in a folder structure based on the session name
   * the program should show the user which of the remote saves have corresponding local copies and only allow them to be synched if the user has a local copy
   * if a user does not have a local copy it will provide instructions to:
    * open start rapture locally
    * create a new game using the session name which exactly matches the remote session name
    * once this is done they will be able to overwrite it with the remote version
   * all save files for a session should be tracked and managed
 * autodetect local save file location. resolve in the following order:
   * Steam path from registry: HKCU\Software\Valve\Steam\SteamPath.
   * [SteamPath]\userdata\[YourSteamID]\1631270\remote\Saved\SaveGames\
     * if there are mulitple and they all have the 1631270 prompt the user
   * C:\Users\[YourUsername]\AppData\Local\StarRupture\Saved\SaveGames\
 * build into a self contained application
 * the save files should not be synchronised directly in the steam save file location instead
   * keep a copy of the repo in %LOCALAPPDATA%\StarRuptureSync\repo
 * the steam save files should only be overwritten when clicks a button "update local save" in the application
 * the git repo version and the save file version should be compared based on file hash

Sessions and Save files are structured as follows:
 * stored in (in order based on which should be used first)
   * C:\Program Files (x86)\Steam\userdata\[YourSteamID]\1631270\remote\Saved\SaveGames\
   * C:\Users\[YourUsername]\AppData\Local\StarRupture\Saved\SaveGames\
 * Are stored in a folder named based on the session name
 * the folder contains mulitple saves for that session and each save consits of 2 files a .met and a .sav file, as per following example
   * 0.met  
   * 0.sav  
   * AutoSave0.met  
   * AutoSave0.sav

synchronisation should follow the following pattern
  * fetch, then reset --hard to origin/main before every compare
  * compare filehash of local save vs current git head based on sessions in repo and all files in each session folder
  * select session 
  * if no local version of session provide instructions
  * show status ie newer version to download or local version is newer
  * download and upload buttons for synch
    * download copies git files to steam folder 
      * verify game is not running before download prompt with a message if it is
    * upload replaces all files in steam folder with git files and pushes these as a new commit
  * oif a push fails fetch again, detect that origin/main advanced, show who pushed (from your commit author/message metadata) and when, and force the user to choose: re-pull and discard my upload, or overwrite theirs.