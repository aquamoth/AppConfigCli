.PHONY: build run restore coverage tools

restore:
	dotnet restore src/AppConfigCli || true

build:
	dotnet build -c Debug src/AppConfigCli || true

run:
	@if [ -z "$$APP_CONFIG_CONNECTION_STRING" ]; then \
		echo "APP_CONFIG_CONNECTION_STRING not set"; exit 2; \
	fi
	dotnet run --project src/AppConfigCli -- --prefix $(prefix) --label $(label)

tools:
	dotnet tool restore

coverage: tools
	@rm -rf coveragereport || true
	dotnet test --collect:"XPlat Code Coverage" -v minimal
	dotnet tool run reportgenerator -reports:tests/**/TestResults/*/coverage.cobertura.xml -targetdir:coveragereport -reporttypes:"Html;HtmlSummary"
	@echo "Coverage report: coveragereport/index.html"
