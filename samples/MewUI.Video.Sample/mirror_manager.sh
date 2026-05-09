#!/bin/bash
#
# Author: Raeseok, Lee
#

# Check if the script is run as root
if [ "$EUID" -ne 0 ]
  then echo "Please run as root or use sudo to execute the script."
  exit
fi

# Function to detect the OS
detect_os () {
    if type lsb_release >/dev/null 2>&1; then
        OS=$(lsb_release -si)
    elif [ -f /etc/os-release ]; then
        . /etc/os-release
        OS=$NAME
    elif [ -f /etc/redhat-release ]; then
        OS=`cat /etc/redhat-release | awk '{print $1}'`
    elif [ -f /etc/lsb-release ]; then
        . /etc/lsb-release
        OS=$DISTRIB_ID
    else
        echo "Cannot identify the OS."
        exit 1
    fi
}

# Korean Ubuntu and CentOS mirror list
declare -A mirrors
mirrors=(
    ["Kaist Mirror"]="https://ftp.kaist.ac.kr"
    ["Kakao"]="https://mirror.kakao.com"
    ["Naver"]="https://mirror.navercorp.com"
    ["Lanet"]="https://ftp.lanet.kr"
    ["Enter manually"]="manual"
    ["Rollback"]="rb"
)

# Define the order of the mirrors
mirror_keys=("Kaist Mirror" "Kakao" "Naver" "Lanet" "Enter manually" "Rollback")

# Function to show mirror list
show_mirrors () {
    echo "Select a mirror to use:"
    local idx=1
    for server in "${mirror_keys[@]}"; do
        echo "[$idx] $server"
        let "idx++"
    done
}

# Change mirror for Ubuntu
change_mirror_ubuntu () {
    if [ "$1" == "Rollback" ]; then
        cp /etc/apt/sources.list.bak /etc/apt/sources.list
        echo "Rollback completed."
        apt update
        exit 0
    fi

    local selected_mirror=${mirrors[$1]}
    if [ "$1" == "Enter manually" ]; then
        while true; do
            read -p "Enter the full mirror URL including protocol and domain (like http://mirror.example.com): " selected_mirror
            if [[ $selected_mirror =~ ^https?://|^ftp:// ]]; then
                break
            else
                echo "Invalid URL. Please make sure the URL includes the protocol (http, https, ftp)."
            fi
        done
        selected_mirror="$selected_mirror/ubuntu"
    else
        selected_mirror="$selected_mirror/ubuntu"
    fi

    if [ -z "$selected_mirror" ]; then
        echo "Invalid selection."
        exit 1
    fi

    # Backup current sources.list
    cp /etc/apt/sources.list /etc/apt/sources.list.bak

    # Change the mirror
    sed -i "s|http[s]\?://[^/]*/ubuntu|$selected_mirror|g" /etc/apt/sources.list
    sed -i "s|ftp://[^/]*/ubuntu|$selected_mirror|g" /etc/apt/sources.list
    apt update

    echo "Mirror changed to $selected_mirror."
}

# Change mirror for CentOS
change_mirror_centos () {
    if [ "$1" == "Rollback" ]; then
        cp -r /etc/yum.repos.d.bak/* /etc/yum.repos.d/
        echo "Rollback completed."
        yum update
        exit 0
    fi

    local selected_mirror=${mirrors[$1]}
    if [ "$1" == "Enter manually" ]; then
        while true; do
            read -p "Enter the full mirror URL including protocol and domain (like http://mirror.example.com): " selected_mirror
            if [[ $selected_mirror =~ ^https?://|^ftp:// ]]; then
                break
            else
                echo "Invalid URL. Please make sure the URL includes the protocol (http, https, ftp)."
            fi
        done
        selected_mirror="$selected_mirror/centos"
    else
        selected_mirror="$selected_mirror/centos"
    fi

    if [ -z "$selected_mirror" ]; then
        echo "Invalid selection."
        exit 1
    fi

    # Backup current repo files
    cp -r /etc/yum.repos.d /etc/yum.repos.d.bak

    # Change the mirror in all .repo files
    for repo_file in /etc/yum.repos.d/*.repo; do
        sed -i "s|^baseurl=http[s]\?://[^/]*/centos|$baseurl=$selected_mirror|g" $repo_file
        sed -i "s|^baseurl=ftp://[^/]*/centos|$baseurl=$selected_mirror|g" $repo_file
    done
    yum update

    echo "Mirror changed to $selected_mirror."
}

# Detect the OS and execute the corresponding logic
detect_os
show_mirrors
read -p "Enter the number of the mirror you want to use: " mirror_idx
selected_key="${mirror_keys[$((mirror_idx-1))]}"
if [[ "$OS" == *"Ubuntu"* ]]; then
    change_mirror_ubuntu "$selected_key"
elif [[ "$OS" == *"CentOS"* ]]; then
    change_mirror_centos "$selected_key"
else
    echo "Unsupported OS."
    exit 1
fi