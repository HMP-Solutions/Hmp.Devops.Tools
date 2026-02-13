# Testing the Webhook Locally

This repository is designed that it will only work with managed identity when deployed to Azure.
That menas the local testing experience is limited to tests the triggers and maybe the parsing logic.
Everything else is and should be handled in the unit test project.

## Using Aspire for Local Testing

### Start the Aspire Host

```powershell
cd Aspire/Host
dotnet run
```

> You can also start Aspire over your debugger. But if you use Visual Studio it will not work. So use VS Code or other IDEs

The output will provide you with the aspire dashboard URL. That you can use to analyze the app.

For detailed instructions for each tool see the specific documentation. For the environment remover function see [EnvironmentRemover.md](tools/EnvironmentRemover.md#debugging)