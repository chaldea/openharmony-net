cmake_minimum_required(VERSION 3.16)
project(dotnet_smoke)
set(CMAKE_CXX_STANDARD 17)
set(MANAGED_LIBRARY "${CMAKE_CURRENT_SOURCE_DIR}/../../../libs/${OHOS_ARCH}/openharmony_smoke.so")
if(NOT EXISTS "${MANAGED_LIBRARY}")
  message(FATAL_ERROR "Publish NativeAotSmoke.csproj before assembling the HAP: ${MANAGED_LIBRARY}")
endif()
add_library(smoke SHARED smoke.cpp)
# NativeAOT shared libraries have no SONAME. Search by file name so DT_NEEDED
# records the packaged library name instead of a build-machine absolute path.
target_link_directories(smoke PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../../libs/${OHOS_ARCH}")
target_link_libraries(smoke PRIVATE "-l:openharmony_smoke.so" libace_napi.z.so)
set_target_properties(smoke PROPERTIES SKIP_BUILD_RPATH TRUE)
