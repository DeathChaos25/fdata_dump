# Koei Tecmo .fdata extractor

This is a tool used to extract/dump files from Koei Tecmo games fdata archive format.  

Usage: Drag and Drop the folder containing all the fdata files into the tool's exe (usually named "Motor"), and it will start the extraction process, please note that depending on the game/your HDD speeds this process can take up anywhere up to 2-3 hours on big games and slow HDDs!  

This program supports extracting in "sessions", before extracting a file, it will check if the output file does not already exist, meaning if a file has already been extracted, the program will skip the extraction process on that file and move on, meaning you can dump your fdata files in "sessions" that you can interrupt until you get all the files dumped.
  

Currently Confirmed working games:
  - Fate/Samurai Remnant
  - Fairy Tail 2
  - Dynasty Warriors: Origin
  - Rise of the Ronin
  - Atelier Yumia
  - Fire Emblem Warriors: Three Hopes (use -FE argument in command line)
  
  
  
# Optional Command Line arguments
- First argument is always expected to be the input path, this cannot be anywhere else
- (Optional) -FE -> toggles FEW Three Hopes dumping compatibility mode
- (Optional) -l (this is a lowercase L) -> disables (most) logging output from the dumping process, leaving only the start and end messages (may help speed up dumping if your PC isn't too powerful)


# Filename Support

Some games (like Atelier Yumia's Demo) will "accidentally" leave behind some debug .fdata files, these contain .name files which give "hints" we can then use to derive *some* filenames from the files.
IF These debug fdata are found in the files, the program will attempt to process them and derive some filenames from them wherever possible.



# File grouping
  
Unlike RDB games, FData games do not have traditional file groupings, to this end, this program will attempt to process well known kidssingletondb files to scan for associated file hashes and use those as a grouping method.


# Pre-Processing before a dump

The program will do a few pre-processing passes before dumping your files to ensure the best possible outcome during the dump process, these are:
- The program will scan for known debug fdata files
- If found, these fdata files will be processed first in isolation
- Once processed, the program will look for all .name files that were dumped from these fdata
- These name files will then be processed to derive as many filenames as possible from them, once finished, the program will output a filelist-fdata-rdb.csv file with all derived filenames
- (This output csv file can be used with Cethleann during file grouping processes by placing the csv file in Cethleann's folder, and then using the "-g fdata" argument)
- The program will then dump specific "system" fdata files in isolation to extract important kidssingletondb files
- These db files will then be processed into a hashlist that will then be used for grouping (i.e. the program will now have a "CharacterEditor" output folder where the contained G1M are character model)
- After all these pre-processing steps, the program will then proceed with dumping the rest of the fdata files

Sample output from [Ceathleann](https://github.com/yretenai/Cethleann) when building a G1T using the provided names list if it exists:
![cmd screenshot](https://i.imgur.com/wvKClRU.png)  

# Donations
If you would like to give any donations, [you can do so through this link](https://ko-fi.com/deathchaos).


# Special Thanks
- [TGE](https://github.com/tge-was-taken) - kidsobjdb parsing (not necessary but useful for grouping files when dumping for a cleaner output)
- [Raytwo](https://github.com/Raytwo) - RDB header info (used to fix Rise of the Ronin support)
- [yretenai](https://github.com/yretenai) - Cethleann
