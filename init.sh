#!/bin/bash

# init.sh - Initialize dependent git repositories
# This script clones or fetches the newest commits from the main branch
# for Bakabase.Infrastructures, LazyMortal, and Bakabase.Updater

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to print colored output
print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to handle repository initialization
init_repo() {
    local repo_name=$1
    local repo_path="../$repo_name"
    
    print_status "Processing repository: $repo_name"
    
    if [ -d "$repo_path" ]; then
        print_status "Repository $repo_name already exists, checking if it's a git repository..."
        
        if [ -d "$repo_path/.git" ]; then
            print_status "Found git repository, fetching latest changes..."
            cd "$repo_path"
            
            # Fetch all branches and tags
            git fetch --all --prune
            
            # Check if main branch exists
            if git show-ref --verify --quiet refs/heads/main; then
                print_status "Switching to main branch and pulling latest changes..."
                git checkout main
                git pull origin main
                print_success "Successfully updated $repo_name"
            else
                print_warning "Main branch not found in $repo_name, checking for master branch..."
                if git show-ref --verify --quiet refs/heads/master; then
                    print_status "Switching to master branch and pulling latest changes..."
                    git checkout master
                    git pull origin master
                    print_success "Successfully updated $repo_name (master branch)"
                else
                    print_error "Neither main nor master branch found in $repo_name"
                    return 1
                fi
            fi
            
            cd - > /dev/null
        else
            print_error "$repo_path exists but is not a git repository"
            return 1
        fi
    else
        print_status "Repository $repo_name does not exist, cloning..."
        
        # Try to clone the repository
        # You may need to adjust these URLs based on your actual repository locations
        case $repo_name in
            "Bakabase.Infrastructures")
                git clone https://github.com/Bakabase/Bakabase.Infrastructures.git "$repo_path"
                ;;
            "LazyMortal")
                git clone https://github.com/Bakabase/LazyMortal.git "$repo_path"
                ;;
            "Bakabase.Updater")
                git clone https://github.com/Bakabase/Bakabase.Updater.git "$repo_path"
                ;;
            *)
                print_error "Unknown repository: $repo_name"
                return 1
                ;;
        esac
        
        if [ $? -eq 0 ]; then
            print_success "Successfully cloned $repo_name"
            
            # Switch to main branch if it exists
            cd "$repo_path"
            if git show-ref --verify --quiet refs/heads/main; then
                git checkout main
                print_success "Switched to main branch for $repo_name"
            elif git show-ref --verify --quiet refs/heads/master; then
                git checkout master
                print_success "Switched to master branch for $repo_name"
            fi
            cd - > /dev/null
        else
            print_error "Failed to clone $repo_name"
            return 1
        fi
    fi
}

# Main execution
print_status "Starting initialization of dependent repositories..."

# List of repositories to initialize
repositories=("Bakabase.Infrastructures" "LazyMortal" "Bakabase.Updater")

# Process each repository
for repo in "${repositories[@]}"; do
    if init_repo "$repo"; then
        print_success "Repository $repo initialized successfully"
    else
        print_error "Failed to initialize repository $repo"
        exit 1
    fi
    echo
done

print_success "All dependent repositories have been initialized successfully!"
print_status "You can now build the Bakabase project with all dependencies available." 