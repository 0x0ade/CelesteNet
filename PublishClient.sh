#!/bin/sh
dotnet build CelesteNet.Client -c Release
rm -rf PubClient CelesteNet.Client.zip
mkdir PubClient
cp everest.pubclient.yaml PubClient/everest.yaml
cp -r CelesteNet.Client/bin/Release/net8.0/CelesteNet.* PubClient
[ -f PubClient/CelesteNet.Client.deps.json ] && rm PubClient/CelesteNet.Client.deps.json
cp -r Dialog PubClient
cp -r Graphics PubClient
cd PubClient; zip -r ../CelesteNet.Client.zip *
