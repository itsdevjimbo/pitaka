pitaka_repo_root_from_script_dir() {
    cd -P -- "$1/.." >/dev/null 2>&1 && pwd
}
