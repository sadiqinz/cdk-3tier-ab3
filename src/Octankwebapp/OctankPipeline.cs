using Amazon.CDK;
using Amazon.CDK.Pipelines;
using Amazon.CDK.AWS.CodeCommit;
using Constructs;

namespace Octankwebapp
{
    public class OctankPipeline : Stack
    {
        internal OctankPipeline(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {            
            // Define the repository
            string reponame = "arn:aws:codecommit:ap-southeast-2:808754908315:OctankWebRepo";

            //Create Pipeline with correct repository
            var pipeline = new CodePipeline(this, "pipeline", new CodePipelineProps
            {
                PipelineName = "OctankPipeline",                
                Synth = new ShellStep("Synth", new ShellStepProps
                {
                    Input = CodePipelineSource.CodeCommit(Repository.FromRepositoryArn(this, "OctankRepo", reponame), "main"),
                    Commands = new string[] { "npm install -g aws-cdk", "cdk synth" }
                })
            });

            //Create Octankwebapp Pipeline Stage
            pipeline.AddStage(new OctankPipelineAppStage(this, "OctankWebAppStage", new StageProps{
                Env = new Environment()                
            }));
        }
    }
}