
LOCAL TESTING 
when data base is setup in my sql run this 

# commands 

# FIRST COMMAND TO RUN ON EC2 when setup everthing 
### dotnet ef migrations add InitialCreate
### dotnet ef database update


# If migrations fail, delete the Migrations folder and retry:


### rm -rf Migrations
### dotnet ef migrations add InitialCreate
### dotnet ef database update

## if permission error 
### xattr -d com.apple.quarantine /Users/shoebiqbal/Desktop/codeCompiler/Online_Assessment/OnlineAssessment.Web/bin/Debug/net8.0/OnlineAssessment.Web