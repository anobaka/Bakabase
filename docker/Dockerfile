# syntax=docker/dockerfile:1.4

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
COPY . .

EXPOSE 34567
EXPOSE 34568
EXPOSE 34569

ENTRYPOINT ["dotnet", "Bakabase.Service.dll"]