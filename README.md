# Banking CLI with plain-English requests

A .NET 10 console banking simulator (chequing and savings, in memory). You can type plain English, such as "Move four dollars from savings to chequing", and get a conversational reply. The original commands (`deposit`, `withdraw`, `transfer`, `balance`, `help`, `?`, `exit`) still work exactly as before.

## 1. Build and run

You need the .NET 10 SDK and Git.

```sh
git clone https://github.com/Anju-Pushkaran/banking-cli-ai.git
cd banking-cli-ai
dotnet build
dotnet test
dotnet run --project BankingCli
```

- `dotnet test` runs 144 automated tests. They use a fake AI, so they need no key and no network.
- Type `exit` to quit the app.
- Without an API key the app still starts. Original commands work, and plain English is switched off (the app says so at startup). To turn it on, follow step 2.

## 2. Turn on plain English (set the API key)

The app reads its settings from environment variables. Nothing is stored in the code or the repository.

| Variable | What it is | Default |
| --- | --- | --- |
| `AI_API_KEY` | Your API key. Required for plain English. | none |
| `AI_BASE_URL` | Base URL of any OpenAI-compatible API. | `https://api.openai.com/v1` |
| `AI_MODEL` | Model name. | `gpt-4o-mini` |
| `SHOW_BANK_LINE` | Set to `1` to also print the simulator's own result under each friendly reply. Optional, handy for checking. | off |

**PowerShell**

```powershell
$env:AI_API_KEY = "your-key"
$env:SHOW_BANK_LINE = "1"
dotnet run --project BankingCli
```

**Bash**

```sh
export AI_API_KEY="your-key"
export SHOW_BANK_LINE=1
dotnet run --project BankingCli
```

Notes:

- Run the app from the same terminal where you set the variables. They last until you close that terminal.
- Visual Studio's F5 does not see variables set in a terminal. Use the terminal, or start Visual Studio from it.
- To keep the key off the screen in PowerShell, enter it at a hidden prompt: `$s = Read-Host "API key" -AsSecureString`, then `$env:AI_API_KEY = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s))`.
- Any other OpenAI-compatible service works by changing `AI_BASE_URL` and `AI_MODEL`, for example a local server at `http://localhost:11434/v1`.

## 3. Example inputs

Plain English:

```text
Move four dollars from savings to chequing
put 20 bucks in my savings
take out ten dollars from chequing
what do I have in savings?
show me both balances
Move fourty five from savings to chequing
Transfer 5 to my savings account          (asks for the missing detail)
```

Original commands:

```text
deposit savings 10
withdraw chequing 5.25
transfer savings chequing 4
balance
help
```

Accounts are `chequing` and `savings`. Amounts are positive with up to two decimals.

## 4. Three checks I ran

The friendly wording varies a little between runs. The amounts, accounts and balances do not. These were run with a real key and `SHOW_BANK_LINE=1`.

### Check 1: successful transfer

Steps: start the app, type `deposit savings 100`, then `Move four dollars from savings to chequing`.

Observed:

```text
bank> deposit savings 100
DEPOSIT $100.00 TO savings | savings=$100.00
bank> Move four dollars from savings to chequing
The transfer of $4.00 from savings to chequing was successful. Savings now has $96.00 and chequing has $4.00.
Bank record: TRANSFER $4.00 FROM savings TO chequing | savings=$96.00 | chequing=$4.00
```

### Check 2: a request that needs clarification

Steps: type `Transfer 5 to my savings account`, then `balance`.

Observed: the source account is missing, so the app asks and no balance changes.

```text
bank> Transfer 5 to my savings account
The transfer request is missing: which account to take it from. No changes were made. Please send the complete request again, for example: "Move $4 from savings to chequing".
```

### Check 3: failure cases

**a) The simulator declines.** Steps: with $4.00 in chequing, type `take out five hundred dollars from chequing`.

Observed: the refusal is explained in plain English and nothing changes.

```text
bank> take out five hundred dollars from chequing
The bank could not process your withdrawal because there are insufficient funds in your chequing account, which currently has $4.00. No changes were made.
Bank record: ERROR INSUFFICIENT_FUNDS | chequing=$4.00
```

**b) The AI is unreachable.** Steps: point the app at an address that does not answer, then type `Move four dollars from savings to chequing` and `balance`.

```powershell
$env:AI_API_KEY = "any-value"
$env:AI_BASE_URL = "http://localhost:9/v1"
dotnet run --project BankingCli
```

Observed: a short note, the simulator's original answer, and no balance change. The app does not crash or hang.

```text
bank> Move four dollars from savings to chequing
(The AI service is unavailable, so only the original commands work right now.)
ERROR UNKNOWN_COMMAND
```

The same fallback was seen with a wrong key and with no key. If the AI fails after a command has already run (while writing the reply), the simulator's result line is shown and the command is not run again. A unit test covers that case.

## How it works

1. **Original commands** go straight to the simulator, as before.
2. **Plain English** goes to the AI first. The AI only returns a small JSON request (action, accounts, amount). The code checks every field and builds one existing command. The AI never touches a balance.
3. If a detail is missing or unclear, the app asks for it and changes nothing.
4. A valid command runs through the simulator **once**. The AI then explains the result in everyday English. The code checks that sentence against the simulator's result (amounts, accounts, balances, and that a refusal reads like a refusal). If the AI call fails or the sentence does not match, the simulator's own result line is shown instead, and the command is never run again.

`BankSession` (the simulator) is unchanged.

## Assumptions and limits

- **No memory between lines.** When something is missing, the app asks for the complete request again. This was confirmed with the hiring team as acceptable.
- **One request at a time.** A message asking for two operations is refused and nothing runs.
- **Exact account names.** Near misses such as "checking" or "saving" get a "Did you mean…?" message. Unrelated names get a message listing the real accounts.
- **Ambiguous amounts** such as "four fifty" are treated as missing.
- **The AI is not infallible.** Its output is validated in code before anything runs. A wrong or unusable answer leads to a message or the original behaviour, never a wrong bank change. The reply check covers numbers, account names and which balance belongs to which account. Amounts written only in words are not checked. Occasionally a correct sentence is rejected, and then the simulator's result line is shown.
- **Wrong account in an original-style command**, such as `deposit visa 10`, goes to the AI first and gets a friendlier message. If the AI is unavailable, the simulator answers `ERROR INVALID_ACCOUNT` as before.
- With `SHOW_BANK_LINE=1`, a rejected AI sentence is also printed on the error stream, which helps when testing.

## How I worked

- **Tools:** Claude Code (Claude Sonnet 5.5) as a pair-programming assistant for planning, code and tests, with each design decision discussed before code was written. At run time the app calls an OpenAI-compatible API, tested with `gpt-4o-mini`.
- **Where it fits:** the assistant helped draft the interpreter, the reply writer, the tests and this README. I reviewed each step, chose the design (routing, no memory between lines, wording) and ran every scenario against the real API myself.
- **Suggestion changed after testing:** the first design let the model ask its own follow-up question. For example, after "Transfer 10.20 to my savings account" the model asked "Which account?", but the app keeps no memory, so the answer "chequing" was treated as a new request. I changed it so the code asks the user to send the complete request again. Verified with the same inputs on the real API and with unit tests.
- **Suggestion rejected:** mapping "checking" to `chequing` automatically. I decided the account must be named correctly, so the app asks "Did you mean chequing?" instead. Verified with unit tests and real runs.
- **Suggestion accepted, then tightened:** checking the AI's sentence against the simulator's result. A later review found gaps (numbers without a `$`, swapped balances, a success-sounding reply to an error), so the check was extended and a unit test was added for each. Real-model sentences from my own runs are kept as regression tests so the stricter check does not reject normal wording.
