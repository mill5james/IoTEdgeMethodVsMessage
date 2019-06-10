ARG BASE_TAG=stretch-slim
ARG CONFIG=Release

FROM microsoft/dotnet:2.2-sdk AS build_Release
WORKDIR /vsdbg

FROM build_Release AS build_Debug
ONBUILD RUN apt-get update && \
			apt-get install --yes unzip && \
			curl -SL https://aka.ms/getvsdbgsh | bash /dev/stdin -v latest -l /vsdbg

FROM build_${CONFIG} AS publish
ARG CONFIG
WORKDIR /src

COPY ["Consumer", "Consumer"]
RUN dotnet publish --configuration ${CONFIG} --output /app "Consumer/Consumer.csproj"

FROM microsoft/dotnet:2.2-runtime-$BASE_TAG as final
WORKDIR /app
COPY --from=publish ["/vsdbg", "/vsdbg"]
COPY --from=publish ["/app", "."]
ENTRYPOINT ["dotnet", "Consumer.dll"]
