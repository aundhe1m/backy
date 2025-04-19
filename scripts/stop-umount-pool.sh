#! /bin/bash

# Loop through all /dev/md* devices
for md_device in /dev/md*; do
    # Check if the device exists
    if [ -e "$md_device" ]; then
        echo "Processing $md_device..."

        # Check if the device is mounted
        mountpoint=$(findmnt -n -o TARGET "$md_device" 2>/dev/null)
        if [ -n "$mountpoint" ]; then
            echo "$md_device is mounted at $mountpoint. Unmounting..."
            sudo umount "$md_device"
            if [ $? -ne 0 ]; then
                echo "Failed to unmount $md_device. Skipping..."
                continue
            fi
        else
            echo "$md_device is not mounted."
        fi

        # Stop the md device
        echo "Stopping $md_device..."
        sudo mdadm --stop "$md_device"
        if [ $? -ne 0 ]; then
            echo "Failed to stop $md_device."
        else
            echo "$md_device stopped successfully."
        fi
    else
        echo "No md devices found."
    fi
done