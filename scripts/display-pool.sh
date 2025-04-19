#! /bin/bash
set -e
echo "-- lsblk --" && lsblk && echo ""
echo "-- Displaying pool metadata --" && cat /var/lib/backy/pool-metadata.json && echo ""
