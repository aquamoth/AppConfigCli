.PHONY: build run restore

restore:
	dotnet restore src/AppConfigCli || true

build:
	dotnet build -c Debug src/AppConfigCli || true

run:
	@if [ -z "$$APP_CONFIG_CONNECTION_STRING" ]; then \
		echo "APP_CONFIG_CONNECTION_STRING not set"; exit 2; \
	fi
	dotnet run --project src/AppConfigCli -- --prefix $(prefix) --label $(label)

