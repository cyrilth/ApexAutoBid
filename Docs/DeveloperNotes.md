# Git Branch Workflow: Squash Merge `develop` into `main`

This project uses a `main` branch and a `develop` branch.

The `develop` branch is used for active work. When the work is ready, a Pull Request is created from `develop` into `main`.

## Why This Cleanup Is Needed

When using **Squash and Merge** on GitHub, GitHub creates a new single commit on `main`.

Example:

```text
develop: A---B---C---D
main:    A---B---S
```

`S` is the squash commit. It contains the same final code changes as `C` and `D`, but Git does not see `C` and `D` as merged into `main`.

Because of this, after a squash merge, the `develop` branch should be reset to match `main`. This keeps future Pull Requests clean and prevents old commits from showing up again.

## After Squash Merging `develop` into `main`

Run these commands:

```bash
git checkout main
git pull origin main

git checkout develop
git reset --hard origin/main

git fetch origin develop:refs/remotes/origin/develop
git push --force-with-lease=develop:origin/develop origin develop
```

## What These Commands Do

### 1. Switch to `main`

```bash
git checkout main
```

Moves your local working branch to `main`.

### 2. Pull the latest `main`

```bash
git pull origin main
```

Updates local `main` with the latest version from GitHub.

This should include the squash merge commit from the Pull Request.

### 3. Switch to `develop`

```bash
git checkout develop
```

Moves back to the `develop` branch.

### 4. Reset `develop` to match `main`

```bash
git reset --hard origin/main
```

Makes local `develop` exactly match `origin/main`.

Warning: this removes any local uncommitted changes and any commits on `develop` that are not on `main`.

### 5. Refresh remote tracking info for `develop`

```bash
git fetch origin develop:refs/remotes/origin/develop
```

Updates the local reference for `origin/develop`.

This helps `--force-with-lease` verify the latest remote state correctly.

### 6. Push the cleaned `develop` branch

```bash
git push --force-with-lease=develop:origin/develop origin develop
```

Updates the remote `develop` branch on GitHub so it matches the cleaned local `develop`.

`--force-with-lease` is safer than plain `--force` because it refuses to overwrite the remote branch if it changed unexpectedly.

## When To Use This

Use this after:

1. Creating a Pull Request from `develop` to `main`
2. Merging the PR using **Squash and Merge**
3. Confirming `main` has the latest merged code

## Normal Development Flow

After cleanup, continue work on `develop`:

```bash
git checkout develop
```

Make code changes, commit them, and push:

```bash
git add .
git commit -m "Your commit message"
git push origin develop
```

Then create a new Pull Request from:

```text
develop → main
```

The new PR should only show the new changes made after the last cleanup.

## Important Warning

Do not run the reset command if you have uncommitted work on `develop`.

Check your status first:

```bash
git status
```

If you have local changes that you want to keep, commit them or stash them before running the reset:

```bash
git stash
```

Then after the reset, you can reapply them if needed:

```bash
git stash pop
```

## Solo Developer Recommendation

For a solo developer using squash merges, this workflow keeps the Git history clean:

```text
Work on develop → PR to main → Squash merge → Reset develop to main → Continue work
```
