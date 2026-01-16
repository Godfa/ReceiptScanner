# Claude Code Guidelines

## Security Best Practices

### Command Line Security
- **Never use secrets directly in command line arguments**
- Always use variables to store sensitive information
- Use the `read` command to securely read secrets into variables
- This prevents secrets from appearing in shell history, process lists, and logs

Example:
```bash
# BAD - secret visible in history
git remote add origin https://user:SECRET_TOKEN@github.com/repo.git

# GOOD - secret read securely
read -s SECRET_TOKEN
git remote add origin https://user:$SECRET_TOKEN@github.com/repo.git
```
