FROM mcr.microsoft.com/dotnet/sdk@sha256:ec9c0a0dc5f60adc2065762050638dd5ed5facfc53716d4580a42d86027c8e80
ENV NUGET_PACKAGES=/opt/teamflow-nuget
WORKDIR /tmp/dependencies
COPY Dependencies.csproj .
RUN dotnet restore Dependencies.csproj --source https://api.nuget.org/v3/index.json -p:NuGetAudit=false && chmod -R a+rX /opt/teamflow-nuget && rm -rf /tmp/dependencies
WORKDIR /workspace
