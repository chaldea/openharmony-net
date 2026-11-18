cmake_minimum_required(VERSION 3.16)
project(dotnet_app)
set(CMAKE_CXX_STANDARD 17)
add_library(dotnet SHARED host.cpp)
# NativeAOT libraries have no SONAME: link by filename, never by absolute path.
target_link_directories(dotnet PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}/../../../libs/${OHOS_ARCH}")
target_link_libraries(dotnet PRIVATE "-l:@ASSEMBLY_NAME@.so" libace_napi.z.so)
set_target_properties(dotnet PROPERTIES SKIP_BUILD_RPATH TRUE)