# 5. Infrastructure and CI/CD

Checked against: frogsmashers `7a5783a`, hindsight `c1e061b`, hootenanny `691b776`.

[Back to the project README](../README.md) · Previous: [Connecting](04-connecting.md)

There are two pipelines. The server deploys itself from GitHub to AWS. The game is built and
published from a developer's own PC, because there is no CI image that builds Unity 2019.2 with
IL2CPP for Windows.

## The server: Hootenanny on AWS

```mermaid
flowchart TB
  subgraph BOOT["infra/bootstrap: applied once, by hand, with admin credentials"]
    TFS["S3 hootenanny-tfstate-account<br/>versioned, encrypted, never public"]
    ART["S3 hootenanny-artifacts-account<br/>server builds, builds/ expire after 30 days"]
    OIDC["IAM OIDC provider<br/>token.actions.githubusercontent.com"]
    PLANR["role hootenanny-ci-plan<br/>pull_request only: ReadOnlyAccess + the state lock"]
    APPLYR["role hootenanny-ci-apply<br/>refs/heads/main only: EC2, logs, CloudWatch, SNS, SSM<br/>in us-east-1. IAM only for hootenanny-server"]
    BUD["Budget hootenanny-monthly<br/>email at 80% spent and 100% forecast"]
    OIDC --- PLANR
    OIDC --- APPLYR
  end
  VARS["GitHub repository variables,<br/>copied by hand from infra/bootstrap's outputs<br/>TF_STATE_BUCKET · AWS_PLAN_ROLE_ARN · AWS_APPLY_ROLE_ARN"]

  subgraph CI["GitHub Actions: hootenanny .github/workflows/ci.yml"]
    TEST["test: dotnet test<br/>every push and pull request"]
    PLAN["plan: pull requests from this repo<br/>terraform plan, posted as a PR comment"]
    DEPLOY["deploy: merge to main, one at a time<br/>1. dotnet publish linux-arm64, single file<br/>2. upload builds/SHA.tar.gz and latest.tar.gz<br/>3. terraform apply infra/server<br/>4. aws ssm send-command: hootenanny-deploy<br/>5. poll up to 10 minutes, fail unless it succeeded"]
    TEST --> PLAN
    TEST --> DEPLOY
  end
  VARS --> CI
  PLAN -. "assumes" .-> PLANR
  DEPLOY -. "assumes" .-> APPLYR
  PLAN -- "state + lock" --> TFS
  DEPLOY -- "state + lock" --> TFS
  DEPLOY -- "server build" --> ART

  subgraph SRV["infra/server: applied by CI, state at server/terraform.tfstate"]
    SG["Security group<br/>UDP 9050 in from anywhere, no SSH, all out"]
    EIP["Elastic IP<br/>= ServerLobby.DefaultServerHost in the game"]
    EC2["EC2 t4g.nano, Amazon Linux 2023 ARM<br/>IMDSv2 only, encrypted 8 GB gp3<br/>first boot: swap, CloudWatch agent, logrotate,<br/>systemd hootenanny.service, hootenanny-deploy script"]
    LOG["log file: /var/log/hootenanny/server.log<br/>one JSON object per line"]
    LG["CloudWatch log group: /hootenanny/server<br/>kept 30 days"]
    AERR["alarm hootenanny-error-logged<br/>metric filter: level = ERROR"]
    AHC["alarm hootenanny-instance-unhealthy<br/>EC2 status check"]
    SNS["SNS topic hootenanny-alerts → email"]
    SG --- EC2
    EIP --- EC2
    EC2 --> LOG
    LOG -- "CloudWatch agent" --> LG
    LG --> AERR
    AERR --> SNS
    AHC --> SNS
  end
  DEPLOY -- "terraform apply" --> SRV
  DEPLOY -- "SSM: run hootenanny-deploy" --> EC2
  EC2 -- "fetches latest.tar.gz,<br/>keeps the previous build" --> ART
```

Things to know:

- A **new server build** is deployed in place through SSM, and the instance stays. A **changed
  `user-data.sh`** replaces the instance, and the Elastic IP moves to the new one. A newer base
  image alone changes nothing (`ignore_changes = [ami]`).
- `terraform destroy` releases the Elastic IP. The game has that address hard-coded
  (`ServerLobby.DefaultServerHost`, `98.80.29.2`), so after a destroy and re-apply the game needs
  a new release with the new address.
- When the game and the server both change their shared protocol, **deploy the server first**: it
  accepts the current and the previous protocol version.
- Log lines: `client.connected`, `room.created`, `room.joined`, `room.introduced`,
  `client.disconnected`. Also one `room.summary` per Room when it closes, and `server.stats`
  every minute.

## The game: built locally, published to itch.io

```mermaid
flowchart TB
  HS["../hindsight<br/>its own CI: dotnet test on every push"]
  HO["../hootenanny<br/>src/Hootenanny.Protocol"]
  UH["tools/update-hindsight.sh<br/>Unity closed, .NET SDK on Windows"]
  UO["tools/update-hootenanny.sh<br/>Unity closed, .NET SDK on Windows"]
  PL["HohimBrueh/Assets/Plugins<br/>Hindsight.Core.dll · Hindsight.Determinism.dll<br/>Hootenanny.Protocol.dll"]
  CC["tools/compile-check.sh<br/>Unity's Roslyn compile → sim-guard.py → state-coverage.py<br/>must pass before every commit that touches game code"]
  B["tools/build.sh dev or release<br/>Unity 2019.2.12f1 batch mode → BuildScript → IL2CPP Windows player<br/>build ID = short commit, plus -dirty → Resources/BuildId.txt<br/>LICENSE.md copied next to the exe"]
  R["tools/release-itch.sh VERSION<br/>refuses uncommitted changes → clean release build →<br/>checks LICENSE.md and THIRD-PARTY-NOTICES.md → butler push,<br/>IL2CPP debug symbols left out → prints the git tag to push"]
  I["itch.io: blemonz/frog-smashers-online:windows<br/>Restricted page, download keys"]
  P["Players<br/>the itch app updates them automatically"]
  HS --> UH --> PL
  HO --> UO --> PL
  PL --> CC --> B --> R --> I --> P
```

One-time setup on each developer PC: Unity 2019.2.12f1 with IL2CPP, Visual Studio C++ tools,
WSL, the .NET SDK, and `tools/patch-il2cpp-sockets.ps1` (as Administrator). Without that patch,
IL2CPP builds can't open sockets with newer Windows SDKs. Publishing also needs butler, logged in.

Hindsight has its own CI (`.github/workflows/test.yml`, `dotnet test` on every push), but nothing
publishes it: the game picks up a new Hindsight only when someone runs `update-hindsight.sh` and
commits the DLLs.

## Code map

| In the diagram | Where |
|---|---|
| Bootstrap stack | `hootenanny: infra/bootstrap/main.tf`, `github_role/main.tf`, `private_bucket/main.tf` |
| Server stack | `hootenanny: infra/server/main.tf`, `variables.tf`, `user-data.sh` |
| Workflow | `hootenanny: .github/workflows/ci.yml` |
| Log lines | `hootenanny: src/Hootenanny.Server/HootServer.cs`, `Log.cs` |
| Game scripts | `frogsmashers: tools/` |
| Build ID | `frogsmashers: HohimBrueh/Assets/Editor/Build/BuildScript.cs`, `HohimBrueh/Assets/Scripts/BuildInfo.cs` |
| Hindsight CI | `hindsight: .github/workflows/test.yml` |
