#!/usr/bin/env bash
# Runs the headless screenshot capture inside the SDK container.
# Installs libfontconfig first: SkiaSharp needs it and the SDK image lacks it.
set -e
apt-get update -qq
apt-get install -y -qq libfontconfig1
cd /src
dotnet run --project tools/CaptureScreens -r linux-x64 --self-contained false -- reference_images
