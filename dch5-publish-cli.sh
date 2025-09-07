#!/bin/bash

echo "🚀 Starting CLI deployment process..."

# start SSH agent and add key
echo "🔑 Starting SSH agent and adding deployment key..."
eval $(ssh-agent -s)
ssh-add /c/.ssh/deepdrft_ed25519
echo "✅ SSH agent configured"

CLI_PROJ="DeepDrftCli"
CLI_APP="deepdrft-cli.tar.gz"

# Publish CLI with framework-dependent single file
echo "🔨 Publishing CLI project for linux-x64..."
dotnet publish $CLI_PROJ -c Release -f net9.0 -o $CLI_PROJ/publish -r linux-x64 \
  --self-contained false \
  -p:PublishSingleFile=true \
  -p:Platform="Any CPU" \
  --verbosity normal

if [ $? -eq 0 ]; then
    echo "✅ CLI project published successfully"
else
    echo "❌ Failed to publish CLI project"
    exit 1
fi

# Eliminate local environment from package
echo "🧹 Removing local environment from package..."
rm -rf $CLI_PROJ/publish/environment
echo "✅ Local environment removed"

# Compress published files
echo "📦 Compressing published files..."
tar -czf $CLI_APP -C $CLI_PROJ/publish .
echo "✅ Package created: $CLI_APP"

# Deploy
REMOTE="deepdrft@dch5.snailbird.net"
CLI_APPROOT="/deepdrft/cli"

echo "🌐 Deploying to remote server: $REMOTE"
echo "📁 Target directory: $CLI_APPROOT"

echo "🗑️ Cleaning existing deployment..."
ssh $REMOTE "rm -rf $CLI_APPROOT/bin/*"
echo "✅ Remote directory cleaned"

echo "📤 Uploading package to remote server..."
scp $CLI_APP $REMOTE:$CLI_APPROOT/$CLI_APP
if [ $? -eq 0 ]; then
    echo "✅ Package uploaded successfully"
else
    echo "❌ Failed to upload package"
    exit 1
fi

echo "📦 Extracting and setting up CLI on remote server..."
ssh $REMOTE "tar -xzf $CLI_APPROOT/$CLI_APP -C $CLI_APPROOT/bin && \
             chmod +x $CLI_APPROOT/bin/DeepDrftCli && \
             rm $CLI_APPROOT/$CLI_APP"
if [ $? -eq 0 ]; then
    echo "✅ CLI extracted and configured on remote server"
else
    echo "❌ Failed to extract CLI on remote server"
    exit 1
fi

# Apply Local Environment (if exists)
echo "🔧 Checking for local environment configuration..."
if ssh $REMOTE "[ -d $CLI_APPROOT/environment ]"; then
    echo "📋 Local environment found, applying configuration..."
    
    # Ensure environment directory exists in the binary location
    ssh $REMOTE "mkdir -p $CLI_APPROOT/bin/environment"
    
    # Copy environment files with better error handling
    if ssh $REMOTE "cp $CLI_APPROOT/environment/* $CLI_APPROOT/bin/environment/ 2>/dev/null"; then
        echo "✅ Local environment configuration applied successfully"
    else
        echo "⚠️  Warning: Some environment files may not have been copied"
    fi
else
    echo "ℹ️  No local environment configuration found - skipping"
fi

echo "🔗 Setting up user-accessible command symlink..."
# Create user-accessible symlink without sudo
ssh $REMOTE "mkdir -p ~/bin && ln -sf $CLI_APPROOT/bin/DeepDrftCli ~/bin/deepdrft"
if [ $? -eq 0 ]; then
    echo "✅ Symlink created successfully"
else
    echo "❌ Failed to create symlink"
    exit 1
fi

echo "🛣️  Ensuring ~/bin is in PATH..."
# Ensure ~/bin is in PATH (add to .bashrc if not present)
ssh $REMOTE "grep -q '~/bin' ~/.bashrc || echo 'export PATH=\"\$HOME/bin:\$PATH\"' >> ~/.bashrc"
echo "✅ PATH configuration updated"

echo "🧹 Cleaning up local files..."
# Clean up
rm -rf ./$CLI_PROJ/publish
rm -f ./$CLI_APP
ssh-agent -k
echo "✅ Local cleanup completed"

echo ""
echo "🎉 CLI deployment complete!"
echo "📝 Note: Run 'source ~/.bashrc' or start a new shell session to activate the deepdrft command in PATH"