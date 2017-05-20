SET GITTAG=release-2.3.4-sr2 
IF NOT EXIST "Xamarin.Forms" (
  git clone https://github.com/xamarin/Xamarin.Forms.git
)
CD Xamarin.Forms
git reset --hard
git checkout tags/%GITTAG%
..\..\src\RoslynApiRefactor\bin\RoslynApiRefactor.exe Xamarin.Forms.sln Xamarin.Forms.Core ..\RenameList.Xamarin.Forms.txt
