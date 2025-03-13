#!/bin/bash
if [ ! -d "./lib" ]; then
    mkdir ./lib
fi

# Function to build and copy for Linux
build_for_linux() {
    cargo build --release --manifest-path ../../rust/plutus-vm-dotnet-rs/Cargo.toml
    cp ../../rust/plutus-vm-dotnet-rs/target/release/libplutus_vm_dotnet_rs.so "./lib/libplutus_vm_dotnet_rs.so"
}

# Function to build and copy for macOS
build_for_macos() {
    cargo build --release --manifest-path ../../rust/plutus-vm-dotnet-rs/Cargo.toml
    cp ../../rust/plutus-vm-dotnet-rs/target/release/libplutus_vm_dotnet_rs.dylib "./lib/libplutus_vm_dotnet_rs.dylib"
}

# Check the operating system
OS="`uname`"
case $OS in
    'Linux')
        # Linux-specific commands
        build_for_linux
    ;;
    'Darwin')
        # macOS-specific commands
        build_for_macos
    ;;
    *) ;;
esac