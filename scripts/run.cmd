@echo off
setlocal
pushd "%~dp0" || exit /b 1
dotnet RestaurantOrders.Api.dll %*
set "result=%errorlevel%"
popd
exit /b %result%
