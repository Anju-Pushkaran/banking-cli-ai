# Banking CLI with plain-English requests

A .NET 10 console banking simulator (chequing and savings, in memory) extended so people can type plain English, such as "Move four dollars from savings to chequing", and get a conversational reply. The original commands, including `help`, `?` and `exit`, work exactly as before.

## How it works

1. **Original commands** (for example `deposit savings 10`) go straight to the simulator, as before.
2. **Plain English** goes to an AI model first. The model only returns a small JSON request (action, accounts, amount). The code validates every field and builds one existing command. The model never touches a balance.
3. If a detail is missing or unclear, the app asks for it and **changes nothing**.
4. A valid command runs through the simulator **once**. The model then explains the result in everyday English. The code checks the sentence against the simulator's result (amounts, accounts, balances, and that a refusal reads like a refusal). If the AI call fails or the sentence does not match, the simulator's original result line is shown instead. The command is never run a second time.

`BankSession` (the simulator) is unchanged.

## Build and run

Requires the .NET 10 SDK.

```powershell
dotnet build
dotnet run --project BankingCli
dotnet test
```

The tests use a fake AI, so they need no key and no network.

Without an API key the app still starts: original commands work and plain English is simply not understood.

## Configure the AI endpoint, model and key

Settings are read from environment variables. Nothing is stored in the code or in the repository.

| Variable | Purpose | Default |
| --- | --- | --- |
| `AI_API_KEY` | API key. Plain-English mode is off without it. | none |
| `AI_BASE_URL` | Base URL of any OpenAI-compatible API. | `https://api.openai.com/v1` |
| `AI_MODEL` | Model name. | `gpt-4o-mini` |
| `SHOW_BANK_LINE` | Set to `1` to also print the simulator's own result under each friendly reply. | off |

PowerShell (the key prompt is hidden, so the key is not left in the terminal):

```powershell
$s = Read-Host "API key" -AsSecureString
$env:AI_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))
$env:AI_MODEL = "gpt-4o-mini"            # optional
$env:AI_BASE_URL = "https://api.openai.com/v1"   # optional
dotnet run --project BankingCli
```

Bash:

```sh
export AI_API_KEY="your-key"
export AI_MODEL="gpt-4o-mini"            # optional
export AI_BASE_URL="https://api.openai.com/v1"   # optional
dotnet run --project BankingCli
```

Run the app from the same terminal where the variables are set. Visual Studio's F5 does not pick up variables set in a terminal.

Any other OpenAI-compatible service works by changing the URL and model, for example a local server at `http://localhost:11434/v1`.

## Example inputs

```text
Move four dollars from savings to chequing
put 20 bucks in my savings
take out ten dollars from chequing
what do I have in savings?
show me both balances
Move fourty five from savings to chequing
Transfer 5 to my savings account          (asks for the missing detail)
```

Original commands still work: `deposit savings 10`, `withdraw chequing 5.25`, `transfer savings chequing 4`, `balance`, `help`, `?`, `exit`. Accounts are `chequing` and `savings`.

## Three checks (run with a real key and `SHOW_BANK_LINE=1`)

The wording of the friendly sentence varies a little between runs. The amounts, accounts and balances do not.

**1. Successful transfer**

Steps: start the app, then type `deposit savings 100`, then `Move four dollars from savings to chequing`.

Observed:

```text
bank> deposit savings 100
DEPOSIT $100.00 TO savings | savings=$100.00
bank> Move four dollars from savings to chequing
The transfer of $4.00 from savings to chequing was successful. Savings now has $96.00 and chequing has $4.00.
Bank record: TRANSFER $4.00 FROM savings TO chequing | savings=$96.00 | chequing=$4.00
```

**2. Request that needs clarification**

Steps: type `Transfer 5 to my savings account`, then `balance`.

Observed: the source account is missing, so the app asks and nothing changes.

```text
bank> Transfer 5 to my savings account
The transfer request is missing: which account to take it from. No changes were made. Please send the complete request again, for example: "Move $4 from savings to chequing".
```

**3. Failure cases**

a) The simulator declines. Steps: with $4.00 in chequing, type `take out five hundred dollars from chequing`.

Observed: the refusal is explained in plain English and the balances are unchanged.

```text
bank> take out five hundred dollars from chequing
The bank could not process your withdrawal because there are insufficient funds in your chequing account, which currently has $4.00. No changes were made.
Bank record: ERROR INSUFFICIENT_FUNDS | chequing=$4.00
```

b) The AI is unreachable. Steps: set `AI_BASE_URL` to `http://localhost:9/v1` (and any key), start the app, type `Move four dollars from savings to chequing`, then `balance`.

Observed: a short note, the simulator's original answer, and no change to any balance. The app does not crash or hang.

```text
bank> Move four dollars from savings to chequing
(The AI service is unavailable, so only the original commands work right now.)
ERROR UNKNOWN_COMMAND
```

The same fallback was observed with a wrong key and with no key. If the AI fails after a command has already run (the friendly-reply call), the simulator's result line is shown and the command is not run again. That case is covered by a unit test.

## Assumptions and limits

- **No memory between lines.** When something is missing, the message asks for the complete request again. This was confirmed with the team as acceptable.
- **One request at a time.** A message that asks for two operations is refused and nothing runs.
- **Account names must be exact** (`chequing`, `savings`). Near misses such as "checking" or "saving" get a "Did you mean…?" message. Unrelated names get a plain message listing the real accounts.
- **Amounts** are positive with at most two decimals. An ambiguous amount such as "four fifty" is treated as missing.
- **The AI is not infallible.** Its output is validated in code before anything runs, and a wrong or unusable answer results in a message or the original behaviour, not a bank change. The reply check covers dollar amounts, numbers, account names and balance-to-account pairing. Amounts written only in words are not checked. A correct sentence can occasionally be rejected, in which case the simulator's result line is shown.
- Original-looking commands with a wrong account name, such as `deposit visa 10`, are sent to the AI first and get a friendlier message. If the AI is unavailable, the simulator answers `ERROR INVALID_ACCOUNT` as before.
- `SHOW_BANK_LINE=1` also prints the AI's rejected sentence on the error stream when a reply is turned down, which helps when testing.

## How I worked

- **Tools:** Claude Code (Claude Sonnet 5.5) as a pair-programming assistant for planning, code and tests, with each design decision discussed before code was written. At run time the app calls an OpenAI-compatible API, tested with `gpt-4o-mini`.
- **Where it fits:** the assistant helped draft the interpreter, the reply writer, the tests and this README. I reviewed each step, chose the design (routing, stateless clarification, wording) and ran every scenario against the real API myself.
- **Suggestion changed after testing:** the first design let the model ask its own follow-up question. For example, after "Transfer 10.20 to my savings account" the model asked "Which account?", but the app keeps no memory, so the answer "chequing" was treated as a new request. I changed it so the code asks the user to send the complete request again. Verified with the same inputs on the real API and with unit tests.
- **Suggestion rejected:** mapping "checking" to `chequing` automatically. I decided the account must be named correctly, so the app asks "Did you mean chequing?" instead. Verified with unit tests and real runs.
- **Suggestion accepted, then tightened:** checking the AI's sentence against the simulator's result. A later review found gaps (numbers without a `$`, swapped balances, a success-sounding reply to an error), so the check was extended and unit tests were added for each. Real-model sentences from my own runs are kept as regression tests so the stricter check does not reject normal wording.
