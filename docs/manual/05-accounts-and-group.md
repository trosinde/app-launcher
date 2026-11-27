# Accounts and group

## Overview

| Account / group | Created | Properties |
|---|---|---|
| `appstart-app` | always | Local account, member of the Administrators group. User rights "Deny log on locally" (`SeDenyInteractiveLogonRight`) and "Deny log on through Remote Desktop Services" (`SeDenyRemoteInteractiveLogonRight`). Random password per PC that nobody knows and that is not stored anywhere. Description: "AppStart: account for approved applications (no interactive sign-in)". |
| `AppStart Users` | always | Local group. Only members may open the pipe of the service, i.e. start applications through AppStart. Otherwise the group has no rights. |
| `appstart-user` | on request | Local standard account without admin rights, member of `AppStart Users`. The installer assigns the password. Description: "AppStart: operator account". |

The applications run as `appstart-app`. The service signs in the account through S4U, without a password. This requires the right "Log on as a batch job", which `appstart-app` has through the Administrators group (Windows default). The profile of the account is located under `C:\Users\appstart-app`; the application stores its user settings (HKCU) there. Operators cannot read it.

## Creating the operator account

**In the wizard:** Step 2, check "Create operator account appstart-user", enter the password twice, "Next".

**Through the command line:**

```text
%SETUP% add-user --name appstart-user
```

The command prompts "Password for appstart-user: " and "Repeat password: ". The input stays hidden. If both are empty or different: "Password empty or not identical.", exit code 2.

For scripts, pass the password as one line through standard input:

```text
%SETUP% add-user --name appstart-user --password-stdin < password.txt
```

Notes:

- With `--name`, you can only create `appstart-user`. Any other name results in "With --name only the account appstart-user can be created.", exit code 2.
- If `appstart-user` already exists, the account and its password remain unchanged.
- The password must meet the password policy of the PC, otherwise "Accounts and group could not be set up: … The password does not meet the password policy of this computer (length, complexity or history)."
- To change the password later: as for any local account, e.g. "Computer Management" → "Local Users and Groups" → `appstart-user` → "Set Password…".

## Adding other users to the group

Instead of `appstart-user`, operators can use their own local or domain account.

**In the wizard:** Step 2, under "Other members of the group AppStart Users", enter the account as `DOMAIN\user`, `.\user` (local) or `user`, "Add", "Next".

**Through the command line:**

```text
%SETUP% add-user --member "DOMAIN\operator01"
```

Exactly one option is allowed: `--name appstart-user` **or** `--member "<Account>"`. Otherwise: "Specify exactly one option: --name appstart-user or --member "DOMAIN\User".", exit code 2.

Only user accounts can be added, no groups such as "Everyone" or "Users": otherwise AppStart would be open to everyone. An account that does not exist, or a group, leads to "Accounts and group could not be set up" with the reason, e.g. "The account "DOMAIN\xy" was not found." or "… is not a user account. Only users can be added to "AppStart Users"."

> **Important:** The membership only takes effect after the user **signs in again**. Until then, the launcher shows "No permission".

## Removing users from the group

- **In the wizard:** Step 2, click ✕ ("Remove member") next to the account, then "Next". This applies to every member of the group, including accounts from earlier setups and accounts entered by hand. Below the list it says: "Members removed with ✕ are taken out of the group when the setup is applied. The change takes effect at the user's next sign-in." `appstart-user` is not in the list; it remains a member as long as the account exists.
- **By hand:** "Computer Management" → "Local Users and Groups" → "Groups" → `AppStart Users` → remove the member. Or in PowerShell as administrator: `Remove-LocalGroupMember -Group "AppStart Users" -Member "DOMAIN\operator01"`.

The command line has no command for removing individual members.

## Warning: administrators without a password

If an enabled account in the Administrators group has no password, step 2 shows a yellow box, e.g. "Administrators without a password: PC-014\service. Anyone can then sign in to this PC as an administrator and remove the protection. Set a password or disable the account; otherwise the verification reports it as a deviation." The setup is still possible. To set a password: `net user <Account> *` in a command prompt opened with "Run as administrator", or disable the account. See check [Administrators with password](06-verification-after-a-service-visit.md#adminpasswort).

## Rules for operation

- **Never add operators to the Administrators group**, not even temporarily for a service visit, without removing the membership afterwards. `verify` reports every member of `AppStart Users` who is an administrator.
- Do not rename or disable `appstart-app` and do not set its password. The account is used only by AppStart.
- Do not remove the sign-in restrictions of `appstart-app`. If a domain Group Policy sets the rights "Deny log on locally" or "Deny log on through Remote Desktop Services", it must include `appstart-app` (or `<PC>\appstart-app`).
- If an account or the group is missing (e.g. deleted by accident), run the setup again ("AppStart – Setup", "Next", "Next"). The setup creates whatever is missing. After the group has been created again, restart the service (see check [Pipe](06-verification-after-a-service-visit.md#pipe)).
