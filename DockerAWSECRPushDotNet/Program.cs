using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Configuration;

namespace DockerAWSECRPushDotNet
{
   class DockerPushProgress : IProgress<JSONMessage>
   {
      public void Report( JSONMessage value )
      {
         if ( value.Progress == null )
            Console.WriteLine( $"Progress {value.Status}" );
         else
            Console.WriteLine( $"Progress. Status {value.Status}, ID {value.ID}, Current {value.Progress.Current}, Total {value.Progress.Total}" );
      }
   }

   class Program
   {
      static async Task Main( string[] args )
      {
         var configBuilder = new ConfigurationBuilder().AddJsonFile( "appsettings.json" );
         var config = configBuilder.Build();

         Console.WriteLine( "Please enter a docker image name from your local docker repository." );
         var dockerImageName = Console.ReadLine();

         Amazon.ECR.AmazonECRClient client = new Amazon.ECR.AmazonECRClient(
                                 config["ECRApiKey"],
                                 config["ECRApiSecret"],
                                 RegionEndpoint.GetBySystemName( config["ECRRegion"] ) );

         // Create a repository in the AWS ECR (Elastic Container Registry)
         var response = await client.CreateRepositoryAsync( new Amazon.ECR.Model.CreateRepositoryRequest()
         {
            RepositoryName = $"packages/{dockerImageName}-{Guid.NewGuid().ToString( "N" )}"
         } );

         if ( response.HttpStatusCode != System.Net.HttpStatusCode.OK )
         {
            Console.WriteLine( $"Failed to create repository in AWS ECR. Status {response.HttpStatusCode}" );
            return;
         }

         Console.WriteLine( $"Repository created. Uri {response.Repository.RepositoryUri}" );

         var packageUri = response.Repository.RepositoryUri;

         // Generate access token to push your local docker image to the newly created repository on ECR
         Console.WriteLine( "Generating access token to push the docker image..." );
         var authResponse = await client.GetAuthorizationTokenAsync( new Amazon.ECR.Model.GetAuthorizationTokenRequest()
         {
         } );

         if ( authResponse.HttpStatusCode != System.Net.HttpStatusCode.OK )
         {
            Console.WriteLine( $"Failed to generate access token for AWS ECR. Status {authResponse.HttpStatusCode}" );
            return;
         }

         var accessToken = Encoding.UTF8.GetString( Convert.FromBase64String( authResponse.AuthorizationData.First().AuthorizationToken ) ).Split( ":" )[1];

         // Create the local docker client
         DockerClient dockerClient = new DockerClientConfiguration().CreateClient();

         // Associate the remote ECR repository Uri as tag to the local docker image
         Console.WriteLine( "Associating remote repository uri with given local docker image..." );
         await dockerClient.Images.TagImageAsync( dockerImageName, new ImageTagParameters()
         {
            RepositoryName = packageUri,
            Force = true
         } );

         // Push the docker image to the remote ECR repository
         await dockerClient.Images.PushImageAsync( packageUri, new ImagePushParameters()
         {
            Tag = "latest"
         }, new AuthConfig()
         {
            Username = "AWS",
            Password = accessToken,
            ServerAddress = authResponse.AuthorizationData.First().ProxyEndpoint
         }, new DockerPushProgress() );

         Console.WriteLine( "Docker push completed...press Enter to quit..." );
         Console.ReadLine();
      }
   }
}
