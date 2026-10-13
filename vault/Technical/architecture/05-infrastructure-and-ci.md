# 5. Infrastructure and CI/CD

Checked against: frogsmashers `7a5783a`, hindsight `c1e061b`, hootenanny `691b776`.

[Back to the vault README](../../README.md) · Previous: [Connecting](04-connecting.md)

There are two pipelines. The server deploys itself from GitHub to AWS. The game is built and
published from a developer's own PC, because there is no CI image that builds Unity 2019.2 with
IL2CPP for Windows.

## The server: Hootenanny on AWS

```text
 ONCE, BY HAND, with admin credentials: infra/bootstrap (its state file stays on that machine)
 ┌──────────────────────────────────────────────────────────────────────────────────────────┐
 │ S3 hootenanny-tfstate-<account>     versioned, encrypted, never public                   │
 │ S3 hootenanny-artifacts-<account>   server builds; builds/ expire after 30 days          │
 │ IAM OIDC provider for token.actions.githubusercontent.com                                │
 │   role hootenanny-ci-plan   only for pull_request: ReadOnlyAccess + the state lock       │
 │   role hootenanny-ci-apply  only for refs/heads/main: EC2, logs, CloudWatch, SNS, SSM in │
 │                             us-east-1; IAM only for hootenanny-server; builds read/write │
 │ Budget hootenanny-monthly   email at 80% spent and 100% forecast                         │
 └──────────────────────────────────────────────────────────────────────────────────────────┘
   its outputs become GitHub repository variables: TF_STATE_BUCKET, AWS_PLAN_ROLE_ARN,
   AWS_APPLY_ROLE_ARN
                                            │
 GITHUB ACTIONS: hootenanny .github/workflows/ci.yml
 ┌──────────────────────────────────────────▼───────────────────────────────────────────────┐
 │ every push and pull request ──► test: dotnet test                                        │
 │                                   │                                                      │
 │ pull request (same repo) ─────────┼─► plan: assume ci-plan ─► terraform plan             │
 │                                   │         ─► posted as a PR comment                    │
 │ merge to main ────────────────────┴─► deploy (one at a time): assume ci-apply            │
 │      1. dotnet publish linux-arm64, single file ─► server.tar.gz                         │
 │      2. upload to s3://hootenanny-artifacts-…/builds/<sha>.tar.gz and latest.tar.gz      │
 │      3. terraform apply infra/server                                                     │
 │      4. aws ssm send-command: wait for cloud-init, run /usr/local/bin/hootenanny-deploy  │
 │      5. poll up to 10 minutes; fail the job unless it succeeded                          │
 └──────────────────────────────────────────┬───────────────────────────────────────────────┘
                                            │ apply + SSM
 AWS us-east-1: infra/server (state in S3 at server/terraform.tfstate, S3-native lock)
 ┌──────────────────────────────────────────▼───────────────────────────────────────────────┐
 │ Security group: UDP 9050 in from anywhere; no SSH (Session Manager instead); all out     │
 │ Elastic IP ─► EC2 t4g.nano, Amazon Linux 2023 ARM, IMDSv2 only, encrypted 8 GB gp3       │
 │   instance role: SSM core · read builds/* · write its own logs                           │
 │   first boot (user-data.sh): 1 GB swap · CloudWatch agent · logrotate ·                  │
 │     systemd hootenanny.service (hardened, MemoryMax 200M, restarts on crash) ·           │
 │     /usr/local/bin/hootenanny-deploy: fetch latest.tar.gz, keep the previous build in    │
 │     /opt/hootenanny.old, restart the service                                             │
 │                                                                                          │
 │ /var/log/hootenanny/server.log (one JSON object per line)                                │
 │   ─► CloudWatch log group /hootenanny/server (30 days)                                   │
 │        ─► metric filter $.level = "ERROR" ─► alarm hootenanny-error-logged ──┐           │
 │   EC2 status check ─────────────────────► alarm hootenanny-instance-unhealthy┤           │
 │                                            SNS topic hootenanny-alerts ◄─────┘─► email   │
 └──────────────────────────────────────────────────────────────────────────────────────────┘
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

```text
 sibling repos on the developer's PC
   ../hindsight ── tools/update-hindsight.sh ──► Assets/Plugins/Hindsight/{Core,Determinism}.dll
   ../hootenanny ─ tools/update-hootenanny.sh ─► Assets/Plugins/Hootenanny/Hootenanny.Protocol.dll
                                                  (Unity closed; .NET SDK on Windows)
                                    │
                                    ▼
 tools/compile-check.sh   Unity's Roslyn compile ─► sim-guard.py (no clock, unsynced random or
                          devices in simulation files) ─► state-coverage.py (every field saved)
                          must pass before every commit that touches game code
                                    │
                                    ▼
 tools/build.sh dev|release   Unity 2019.2.12f1 batch mode ─► BuildScript ─► IL2CPP Windows player
                              build ID = short commit (+ -dirty) ─► Resources/BuildId.txt
                              LICENSE.md copied next to the exe
                              dev: builds/dev (diagnostics, several copies, Sync Test Rounds)
                              release: builds/release
                                    │
                                    ▼
 tools/release-itch.sh VERSION
     refuses uncommitted changes ─► clean release build ─► checks LICENSE.md and
     THIRD-PARTY-NOTICES.md ─► butler push builds/release blemonz/frog-smashers-online:windows
     --userversion VERSION (IL2CPP debug symbols left out) ─► prints the git tag to push
                                    │
                                    ▼
 itch.io (Restricted page, download keys) ─► players; the itch app updates them automatically
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
