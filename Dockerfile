FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/src/ProfitHub.Api/ ProfitHub.Api/
RUN dotnet publish ProfitHub.Api -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# QuestPDF renders via SkiaSharp, which needs native font libraries on the
# Debian-based aspnet runtime image (Render). Without these, PDF generation crashes.
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 && rm -rf /var/lib/apt/lists/*
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProfitHub.Api.dll"]
