Remove-Item -Recurse -Path PubClient
Remove-Item -Path CelesteNet.Client.zip
New-Item -ItemType directory -Path PubClient
Copy-Item -Path everest.pubclient.yaml -Destination PubClient/everest.yaml
Copy-Item -Recurse -Path CelesteNet.Client/bin/Release/net452/* -Destination PubClient
Copy-Item -Recurse -Path Dialog -Destination PubClient
Copy-Item -Recurse -Path Graphics -Destination PubClient
Compress-Archive -Path PubClient/* -DestinationPath CelesteNet.Client.zip
