#!/bin/bash

# MicroPlumberd Release Script
# Automates version tagging and triggering NuGet package publishing

set -e

# Check if running in interactive terminal
check_interactive_terminal() {
    if [[ ! -t 0 ]] || [[ ! -t 1 ]]; then
        echo "# This script requires an interactive terminal for user prompts and confirmations."
        echo "# Please run this script from a terminal session, not from CI/CD or automated context."
        echo "# For automated releases, use the GitHub Actions workflow or implement a non-interactive version."
        return 1
    fi
    return 0
}

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Functions
print_usage() {
    echo -e "${BLUE}MicroPlumberd Release Script${NC}"
    echo
    echo "Usage: ./release.sh [VERSION] [OPTIONS]"
    echo
    echo "Arguments:"
    echo "  VERSION     4-part version (e.g., 1.0.122.154, 1.1.0.0)"
    echo "              If not provided, script will auto-increment build number"
    echo
    echo "Options:"
    echo "  -m, --message TEXT    Release notes/message (optional)"
    echo "  -b, --build           Auto-increment build number (default)"
    echo "  -p, --patch           Auto-increment patch version"
    echo "  -n, --minor           Auto-increment minor version"
    echo "  -M, --major           Auto-increment major version"
    echo "  -y, --yes             Auto-confirm release without prompts"
    echo "  --dry-run             Show what would be done without executing"
    echo "  -h, --help            Show this help message"
    echo
    echo "Examples:"
    echo "  ./release.sh 1.0.123.155                       # Release specific version"
    echo "  ./release.sh 1.0.123.155 -m \"Bug fixes\"       # With release notes"
    echo "  ./release.sh --build -m \"New features\"        # Auto-increment build (default)"
    echo "  ./release.sh --patch -m \"API changes\"         # Auto-increment patch"
    echo "  ./release.sh --minor -y -m \"New feature\"      # Auto-confirm release"
    echo "  ./release.sh --dry-run                         # Preview next build release"
}

print_error() {
    echo -e "${RED}Error: $1${NC}" >&2
}

print_warning() {
    echo -e "${YELLOW}Warning: $1${NC}"
}

print_success() {
    echo -e "${GREEN}$1${NC}"
}

print_info() {
    echo -e "${BLUE}$1${NC}"
}

# Get the latest version tag
get_latest_version() {
    git tag --sort=-version:refname | grep -E '^micro-plumberd/[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$' | head -n1 | sed 's/^micro-plumberd\///' || echo "1.0.0.0"
}

# Increment version
increment_version() {
    local version=$1
    local part=$2

    IFS='.' read -r -a parts <<< "$version"
    local major=${parts[0]:-1}
    local minor=${parts[1]:-0}
    local patch=${parts[2]:-0}
    local build=${parts[3]:-0}

    case $part in
        "major")
            major=$((major + 1))
            minor=0
            patch=0
            build=0
            ;;
        "minor")
            minor=$((minor + 1))
            patch=0
            build=0
            ;;
        "patch")
            patch=$((patch + 1))
            build=0
            ;;
        "build"|*)
            build=$((build + 1))
            ;;
    esac

    echo "$major.$minor.$patch.$build"
}

# Validate 4-part version
validate_version() {
    local version=$1
    if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
        print_error "Invalid version format: $version. Expected format: X.Y.Z.W (e.g., 1.0.122.154)"
        return 1
    fi
    return 0
}

# Check if version already exists
check_version_exists() {
    local version=$1
    if git tag --list | grep -q "^micro-plumberd/$version$"; then
        print_error "Version micro-plumberd/$version already exists!"
        echo "Existing tags:"
        git tag --sort=-version:refname | head -5
        return 1
    fi
    return 0
}

# Initialize git configuration for cross-platform compatibility
init_git_config() {
    # Set core.autocrlf to false to prevent line ending issues between Windows/Linux
    git config --local core.autocrlf false 2>/dev/null || true

    # Set core.filemode to false to ignore file permission changes (Windows/WSL compatibility)
    git config --local core.filemode false 2>/dev/null || true

    # Ensure consistent behavior for git status and diff operations
    git config --local status.submodulesummary false 2>/dev/null || true

    # Set safe directory (helps with WSL/Windows shared folders)
    git config --global --add safe.directory "$(git rev-parse --show-toplevel)" 2>/dev/null || true
}

# Check git status
check_git_status() {
    local auto_confirm="$1"

    # Initialize git config for cross-platform compatibility
    init_git_config

    if ! git diff-index --quiet HEAD --; then
        print_error "Working directory is not clean. Please commit or stash changes first."
        echo
        echo "Uncommitted changes:"
        git status --porcelain
        return 1
    fi

    # Check if we're on master/main
    local branch=$(git branch --show-current)
    if [[ "$branch" != "master" && "$branch" != "main" ]]; then
        print_warning "You're not on master/main branch (current: $branch)"
        if [[ "$auto_confirm" != "true" ]]; then
            read -p "Continue anyway? [y/N]: " -n 1 -r
            echo
            if [[ ! $REPLY =~ ^[Yy]$ ]]; then
                print_error "Aborted by user"
                return 1
            fi
        else
            print_info "Auto-confirming: continuing on $branch branch"
        fi
    fi

    # Check for unpushed commits
    git fetch origin "$branch" 2>/dev/null || true
    local unpushed_commits=$(git rev-list --count origin/"$branch"..HEAD 2>/dev/null || echo "0")
    if [[ "$unpushed_commits" -gt 0 ]]; then
        print_error "There are $unpushed_commits unpushed commit(s) on branch $branch"
        echo
        echo "Unpushed commits:"
        git log --oneline origin/"$branch"..HEAD
        echo
        print_info "Please push your commits first:"
        print_info "  git push origin $branch"
        return 1
    fi

    return 0
}

# Create and push tag
create_release() {
    local version=$1
    local message=$2
    local dry_run=$3

    local tag="micro-plumberd/$version"

    print_info "Creating release $tag..."

    if [[ "$dry_run" == "true" ]]; then
        echo -e "${YELLOW}[DRY RUN]${NC} Would create tag: $tag"
        if [[ -n "$message" ]]; then
            echo -e "${YELLOW}[DRY RUN]${NC} With message: $message"
        fi
        echo -e "${YELLOW}[DRY RUN]${NC} Would push to origin"
        return 0
    fi

    # Create tag
    if [[ -n "$message" ]]; then
        git tag -a "$tag" -m "$message"
        print_success "✓ Created annotated tag $tag with message"
    else
        git tag "$tag"
        print_success "✓ Created tag $tag"
    fi

    # Push tag
    print_info "Pushing tag to origin..."
    git push origin "$tag"
    print_success "✓ Pushed tag $tag to origin"

    # Show next steps
    echo
    print_info "🚀 Release $tag created successfully!"
    echo
    echo "Next steps:"
    echo "1. Monitor the GitHub Actions workflow: https://github.com/modelingevolution/micro-plumberd/actions"
    echo "2. Verify NuGet packages are published to NuGet.org:"
    echo "   - MicroPlumberd $version"
    echo "   - MicroPlumberd.Services $version"
    echo "   - MicroPlumberd.Services.Cron $version"
    echo "   - MicroPlumberd.Encryption $version"
    echo "   - MicroPlumberd.Protobuf $version"
    echo "   - MicroPlumberd.Testing $version"
    echo "   - MicroPlumberd.Services.Uniqueness $version"
    echo "   - MicroPlumberd.Services.ProcessManagers $version"
    echo "   - MicroPlumberd.Services.Grpc.DirectConnect $version"
    echo "   - MicroPlumberd.Services.Identity $version"
    echo "   - MicroPlumberd.Services.Cron.Ui $version"
    echo "3. Check packages on NuGet.org: https://www.nuget.org/profiles/modelingevolution"

    return 0
}

# Main script
main() {
    local version=""
    local message=""
    local increment_type="build"
    local dry_run="false"
    local auto_confirm="false"
    local needs_interactive="true"

    # Parse arguments first to check for help, dry-run, or auto-confirm
    while [[ $# -gt 0 ]]; do
        case $1 in
            -h|--help)
                print_usage
                exit 0
                ;;
            --dry-run)
                dry_run="true"
                needs_interactive="false"
                shift
                ;;
            -y|--yes)
                auto_confirm="true"
                needs_interactive="false"
                shift
                ;;
            -m|--message)
                message="$2"
                shift 2
                ;;
            -b|--build)
                increment_type="build"
                shift
                ;;
            -p|--patch)
                increment_type="patch"
                shift
                ;;
            -n|--minor)
                increment_type="minor"
                shift
                ;;
            -M|--major)
                increment_type="major"
                shift
                ;;
            -*)
                print_error "Unknown option: $1"
                print_usage
                exit 1
                ;;
            *)
                if [[ -z "$version" ]]; then
                    version="$1"
                else
                    print_error "Multiple versions specified: $version and $1"
                    exit 1
                fi
                shift
                ;;
        esac
    done

    # If no arguments provided, show helpful guidance
    if [[ -z "$version" ]] && [[ "$increment_type" == "build" ]] && [[ -z "$message" ]] && [[ "$dry_run" == "false" ]] && [[ "$auto_confirm" == "false" ]]; then
        echo "No arguments provided. Here are some examples:"
        echo
        print_usage
        echo
        echo "For non-interactive environments:"
        echo "  ./release.sh --dry-run                         # Preview release"
        echo "  ./release.sh --build -y -m \"Bug fixes\"        # Auto-confirm release"
        exit 1
    fi

    # Check if running in interactive terminal (only when interactive input is needed)
    if [[ "$needs_interactive" == "true" ]] && ! check_interactive_terminal; then
        echo
        echo "To run this script in non-interactive mode:"
        echo "  ./release.sh --dry-run                       # Preview release"
        echo "  ./release.sh --minor -y -m \"New feature\"    # Auto-confirm release"
        exit 1
    fi

    # Check if we're in a git repository
    if ! git rev-parse --git-dir > /dev/null 2>&1; then
        print_error "Not in a git repository"
        exit 1
    fi

    # Get current version if not specified
    if [[ -z "$version" ]]; then
        local current_version=$(get_latest_version)
        version=$(increment_version "$current_version" "$increment_type")
        print_info "Auto-incrementing $increment_type version: $current_version → $version"
    fi

    # Validate version
    if ! validate_version "$version"; then
        exit 1
    fi

    # Check if version already exists
    if ! check_version_exists "$version"; then
        exit 1
    fi

    # Check git status (skip for dry run)
    if [[ "$dry_run" != "true" ]]; then
        if ! check_git_status "$auto_confirm"; then
            exit 1
        fi
    fi

    # Show summary
    echo
    print_info "Release Summary:"
    echo "  Version: micro-plumberd/$version"
    echo "  Message: ${message:-"(none)"}"
    echo "  Branch:  $(git branch --show-current)"
    echo "  Commit:  $(git rev-parse --short HEAD)"
    if [[ "$dry_run" == "true" ]]; then
        echo -e "  Mode:    ${YELLOW}DRY RUN${NC}"
    fi
    echo

    # Confirm (skip for dry run and auto-confirm)
    if [[ "$dry_run" != "true" ]] && [[ "$auto_confirm" != "true" ]]; then
        read -p "Create release? [y/N]: " -n 1 -r
        echo
        if [[ ! $REPLY =~ ^[Yy]$ ]]; then
            print_error "Aborted by user"
            exit 1
        fi
    elif [[ "$auto_confirm" == "true" ]] && [[ "$dry_run" != "true" ]]; then
        print_info "Auto-confirming release creation"
    fi

    # Create release
    create_release "$version" "$message" "$dry_run"
}

# Run main function
main "$@"
