# Welcome to your CDK C# project!

This is a blank project for CDK development with C#.

The `cdk.json` file tells the CDK Toolkit how to execute your app.

It uses the [.NET Core CLI](https://docs.microsoft.com/dotnet/articles/core/) to compile and execute your project.

## Useful commands

* `dotnet build src` compile this app
* `cdk deploy`       deploy this stack to your default AWS account/region
* `cdk diff`         compare deployed stack with current state
* `cdk synth`        emits the synthesized CloudFormation template


# Steps to follow
1. Create an IAM user to be used for CodeCommit
2. Create CodeCommit repository
3. Create EC2 Instance KMS Key for access to server if required
4. Create ACM Certificate and specify the ARN
5. Specify Secret Manager's ARN in startup script
6. Create Memcached Cluster
7. Create WAF and attached to ALB

# Steps to fix Target Group issue in Code Deploy
1. Refresh to choose the correct ASB
2. Re-Create ASG after Code Deploy has been created

