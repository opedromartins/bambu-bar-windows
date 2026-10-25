FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2
RUN apt-get update && apt-get install -y --no-install-recommends blender nsis zip python3 xvfb xauth libx11-6 libice6 libsm6 libfontconfig1 libfreetype6 libglib2.0-0 libegl1 libgl1-mesa-dri \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir /nuget && chmod 777 /nuget
ENV NUGET_PACKAGES=/nuget DOTNET_CLI_HOME=/tmp/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src
