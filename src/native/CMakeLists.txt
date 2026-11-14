# Hammer-MCP native part: injector + per-build adapters. Windows PE only.
# Linux:   cmake -S src/native -B build/native-x64 -G "Unix Makefiles" -DCMAKE_TOOLCHAIN_FILE=src/native/cmake/clang-cl-xwin.cmake -DHMCP_ARCH=x86_64
# Windows: cmake -S src/native -B build/native-x64 -A x64   (MSVC, the reference build)
cmake_minimum_required(VERSION 3.20)
project(HammerMcpNative VERSION 0.1.0 LANGUAGES C CXX RC)

if(NOT WIN32)
  message(FATAL_ERROR "Windows targets only (use cmake/clang-cl-xwin.cmake to cross-compile)")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")  # static CRT: nothing to install next to Hammer
# sources are UTF-8 ("…" in UI strings); MSVC would read them as the ANSI code page otherwise
add_compile_options($<$<COMPILE_LANGUAGE:C,CXX>:/utf-8>)
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX
  HMCP_NATIVE_VERSION="${PROJECT_VERSION}")
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
  set(HMCP_BITS 64)
else()
  set(HMCP_BITS 32)
endif()

# Builds that have an adapter, generated from the same registry the C# server uses (no hand-kept second list).
find_package(Python3 REQUIRED COMPONENTS Interpreter)
set(HMCP_REGISTRY ${CMAKE_CURRENT_SOURCE_DIR}/../registry/hammers.json)
set(HMCP_ALLOWED ${CMAKE_CURRENT_BINARY_DIR}/generated/allowed_builds.h)
add_custom_command(OUTPUT ${HMCP_ALLOWED}
  COMMAND ${Python3_EXECUTABLE} ${CMAKE_CURRENT_SOURCE_DIR}/tools/gen_allowed_builds.py ${HMCP_REGISTRY} ${HMCP_ALLOWED}
  DEPENDS ${HMCP_REGISTRY} ${CMAKE_CURRENT_SOURCE_DIR}/tools/gen_allowed_builds.py
  COMMENT "allowed_builds.h from registry/hammers.json")
add_custom_target(hmcp_allowed DEPENDS ${HMCP_ALLOWED})

add_library(hmcp_common INTERFACE)
target_include_directories(hmcp_common INTERFACE common ${CMAKE_CURRENT_BINARY_DIR}/generated)
target_link_libraries(hmcp_common INTERFACE bcrypt advapi32)
add_dependencies(hmcp_common hmcp_allowed)

add_executable(hmcp-injector injector/main.cpp injector/injector.rc)
set_target_properties(hmcp-injector PROPERTIES OUTPUT_NAME "hmcp-injector${HMCP_BITS}")
target_link_libraries(hmcp-injector PRIVATE hmcp_common)

# one adapter DLL per build id (registry injectAdapter); only the bitness that matches the build is produced
function(hmcp_adapter id src_dir description)
  add_library(${id} SHARED ${src_dir}/adapter.cpp ${src_dir}/adapter.rc)
  set_target_properties(${id} PROPERTIES OUTPUT_NAME "hmcp-adapter-${id}")
  target_compile_definitions(${id} PRIVATE HMCP_ADAPTER_ID="${id}")
  set_source_files_properties(${src_dir}/adapter.rc TARGET_DIRECTORY ${id} PROPERTIES
    COMPILE_DEFINITIONS "HMCP_ADAPTER_FILE=\"hmcp-adapter-${id}.dll\";HMCP_ADAPTER_DESC=\"${description}\"")
  target_link_libraries(${id} PRIVATE hmcp_common)
endfunction()

# generic-*: layers 0-1 (pipe + MCP menu) for every Source 1 Hammer with a Win32 menu in the registry;
# hpp-8873-*: the same plus build-specific code from layer 2 on (selected by HMCP_ADAPTER_ID)
if(HMCP_BITS EQUAL 64)
  hmcp_adapter(generic-x64 adapters/base "Hammer-MCP adapter (generic, x64)")
  hmcp_adapter(hpp-8873-x64 adapters/base "Hammer-MCP adapter for Hammer++ 8873 x64")
else()
  hmcp_adapter(generic-x86 adapters/base "Hammer-MCP adapter (generic, x86)")
  hmcp_adapter(hpp-8873-x86 adapters/base "Hammer-MCP adapter for Hammer++ 8873 x86")
endif()
