# start SSH agent and add key
eval $(ssh-agent -s)
ssh-add /c/.ssh/deepdrft_ed25519

CONTENT_PROJ="DeepDrftContent"
WEB_PROJ="DeepDrftWeb"
WEB_SERVICES_PROJ="DeepDrftWeb.Services"
CONTENT_APP="deepdrft-content.tar.gz"
WEB_APP="deepdrft-web.tar.gz"

# Restore and Publish
dotnet publish $CONTENT_PROJ -c Release -f net9.0 -o $CONTENT_PROJ/publish -r linux-x64 -p:Platform="Any CPU" --verbosity normal
dotnet publish $WEB_PROJ -c Release -f net9.0 -o $WEB_PROJ/publish -r linux-x64 -p:Platform="Any CPU" --verbosity normal

# Check if migrations are needed
WEB_MIG="deepdrft-migrations.sql"
REMOTE="deepdrft@dch5.snailbird.net"
WEB_APPROOT="/deepdrft/web"

LATEST_MIGRATION=$(dotnet ef migrations list --project $WEB_SERVICES_PROJ --context DeepDrftContext --no-build | tail -1)
REMOTE_MIGRATION=$(ssh $REMOTE "sqlite3 $WEB_APPROOT/Database/deepdrft.db 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;'" 2>/dev/null || echo "")

if [ "$LATEST_MIGRATION" != "$REMOTE_MIGRATION" ]; then
    echo "Generating migration script from $REMOTE_MIGRATION to $LATEST_MIGRATION..."
    if [ -z "$REMOTE_MIGRATION" ]; then
        dotnet ef migrations script --project $WEB_SERVICES_PROJ --context DeepDrftContext --output $WEB_MIG --verbose --no-build
    else
        dotnet ef migrations script $REMOTE_MIGRATION --project $WEB_SERVICES_PROJ --context DeepDrftContext --output $WEB_MIG --verbose --no-build
    fi
    APPLY_MIGRATIONS=true
else
    echo "Database is up to date."
    APPLY_MIGRATIONS=false
fi

# Compress published files
tar -czf $CONTENT_APP -C $CONTENT_PROJ/publish .
tar -czf $WEB_APP -C $WEB_PROJ/publish .

# Deploy
CONTENT_APPROOT="/deepdrft/api/content"

ssh $REMOTE "rm -rf $CONTENT_APPROOT/bin/*"
ssh $REMOTE "rm -rf $WEB_APPROOT/bin/*"
scp $CONTENT_APP $REMOTE:$CONTENT_APPROOT/$CONTENT_APP
if [ "$APPLY_MIGRATIONS" = true ]; then
    scp $WEB_MIG $REMOTE:$WEB_APPROOT/$WEB_MIG
fi
scp $WEB_APP $REMOTE:$WEB_APPROOT/$WEB_APP
ssh $REMOTE "tar -xzf $CONTENT_APPROOT/$CONTENT_APP -C $CONTENT_APPROOT/bin && rm $CONTENT_APPROOT/$CONTENT_APP"
ssh $REMOTE "tar -xzf $WEB_APPROOT/$WEB_APP -C $WEB_APPROOT/bin && rm $WEB_APPROOT/$WEB_APP"

# Apply Local Environment
ssh $REMOTE "cp $CONTENT_APPROOT/environment/* $CONTENT_APPROOT/bin/environment"

# Apply database migrations on server
if [ "$APPLY_MIGRATIONS" = true ]; then
    ssh $REMOTE "sqlite3 $WEB_APPROOT/Database/deepdrft.db < $WEB_APPROOT/$WEB_MIG && rm $WEB_APPROOT/$WEB_MIG"
fi

# Restart the service
ssh $REMOTE "$CONTENT_APPROOT/restart.sh"
ssh $REMOTE "$WEB_APPROOT/restart.sh"

# Clean up
rm -rf ./$CONTENT_PROJ/publish
rm -f ./$CONTENT_APP
rm -rf ./$WEB_PROJ/publish
rm -f ./$WEB_APP
if [ "$APPLY_MIGRATIONS" = true ]; then
    rm -f ./$WEB_MIG
fi
ssh-agent -k
