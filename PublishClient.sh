#!/bin/sh
dotnet build CelesteNet.Client -c Release
rm -rf PubClient CelesteNet.Client.zip
mkdir PubClient
cp everest.pubclient.yaml PubClient/everest.yaml
cp -r CelesteNet.Client/bin/Release/net452/* PubClient
cp -r Dialog PubClient
cp -r Graphics PubClient
cd PubClient; zip -r ../CelesteNet.Client.zip *
