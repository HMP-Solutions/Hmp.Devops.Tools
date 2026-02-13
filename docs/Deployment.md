# Deployment

## Prerequisites

- Install the [aspire cli](https://aspire.dev/get-started/install-cli/)
- Install [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli)
- Install [dotnet 10 SDK](https://dot.net/download)
- Install [Azure Funciton CLI](https://learn.microsoft.com/en-us/azure/azure-functions/functions-run-local#install-the-azure-functions-core-tools)
- Login to Azure CLI with `az login` and make sure you have access to the subscription where you want to deploy the function app.

## Interactive Deployment

Run `aspire deploy` in the folder `./aspire/AppHost/`.
Follow the instructions on your terminal.

## CI/CD Deployment

Configure the parameters via environment variables like:

```pwsh
$env:Parameters__AzureSubscriptionId="your subscription id"
```

Run `aspire deploy --non-interactive` in the folder `./aspire/AppHost/`.

## After Deployment

Some tools maybe need after deployment additional configuration. For that look in the tool specific documentation.