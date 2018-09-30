FROM microsoft/dotnet:2.1-aspnetcore-runtime AS base
WORKDIR /app
EXPOSE 80

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY ["istio-abtest.csproj", "./"]
RUN dotnet restore "./istio-abtest.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "istio-abtest.csproj" -c Release -o /app

FROM build AS publish
RUN dotnet publish "istio-abtest.csproj" -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "istio-abtest.dll"]
