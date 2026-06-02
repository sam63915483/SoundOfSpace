using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace CursedToysII
{
    public class CharacterSwitcherManager : MonoBehaviour
    {
        [Header("Character Setup")]
        [Tooltip("Drag all your characters here in order")]
        public List<CharacterData> characters = new List<CharacterData>();
        
        [Header("Settings")]
        [Tooltip("Which character to start with (0 = first character)")]
        public int startingCharacterIndex = 0;
        
        [Header("Input")]
        [Tooltip("Key to cycle through characters")]
        public Key switchKey = Key.Tab;
        
        [Header("Auto Setup")]
        [Tooltip("Try to automatically find Third Person Controllers on Start")]
        public bool autoFindCharactersOnStart = false;
        
        private int currentCharacterIndex = 0;
        private Keyboard keyboard;
        
        void Start()
        {
            // Input System'den keyboard referansını al
            keyboard = Keyboard.current;
            
            if (autoFindCharactersOnStart)
            {
                AutoFindCharacters();
            }
            
            SetupCharacters();
            SwitchToCharacter(startingCharacterIndex);
        }
        
        void Update()
        {
            // Input System ile doğrudan tuş kontrolü
            if (keyboard != null && keyboard[switchKey].wasPressedThisFrame)
            {
                SwitchToNextCharacter();
            }
        }
        
        void AutoFindCharacters()
        {
            MonoBehaviour[] controllers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            
            characters.Clear();
            
            int characterCount = 0;
            foreach (var controller in controllers)
            {
                string typeName = controller.GetType().Name.ToLower();
                if (typeName.Contains("thirdperson") || typeName.Contains("controller") || 
                    typeName.Contains("player") || typeName.Contains("character"))
                {
                    if (controller is CharacterSwitcherManager) continue;
                    
                    CharacterData newCharacter = new CharacterData();
                    newCharacter.characterName = controller.gameObject.name;
                    newCharacter.characterObject = controller.gameObject;
                    newCharacter.thirdPersonController = controller;
                    
                    newCharacter.animator = controller.GetComponent<Animator>();
                    
                    Camera cam = controller.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        newCharacter.characterCamera = cam.gameObject;
                    }
                    
                    characters.Add(newCharacter);
                    characterCount++;
                }
            }
            
            Debug.Log($"Auto-found {characterCount} characters!");
        }
        
        public void PopulateCharactersList()
        {
            MonoBehaviour[] controllers = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            
            characters.Clear();
            
            int characterCount = 0;
            foreach (var controller in controllers)
            {
                string typeName = controller.GetType().Name.ToLower();
                if (typeName.Contains("thirdperson") || typeName.Contains("controller") || 
                    typeName.Contains("player") || typeName.Contains("character"))
                {
                    if (controller is CharacterSwitcherManager) continue;
                    
                    CharacterData newCharacter = new CharacterData();
                    newCharacter.characterName = controller.gameObject.name;
                    newCharacter.characterObject = controller.gameObject;
                    newCharacter.thirdPersonController = controller;
                    
                    newCharacter.animator = controller.GetComponent<Animator>();
                    
                    Camera cam = controller.GetComponentInChildren<Camera>();
                    if (cam != null)
                    {
                        newCharacter.characterCamera = cam.gameObject;
                    }
                    
                    characters.Add(newCharacter);
                    characterCount++;
                }
            }
            
            Debug.Log($"Populated inspector with {characterCount} characters! You can now reorder them manually.");
        }
        
        void SetupCharacters()
        {
            for (int i = 0; i < characters.Count; i++)
            {
                if (characters[i].characterObject != null)
                {
                    if (characters[i].thirdPersonController == null)
                    {
                        characters[i].thirdPersonController = characters[i].characterObject.GetComponent<MonoBehaviour>();
                    }
                    
                    if (characters[i].characterCamera == null)
                    {
                        Camera cam = characters[i].characterObject.GetComponentInChildren<Camera>();
                        if (cam != null)
                        {
                            characters[i].characterCamera = cam.gameObject;
                        }
                    }
                }
            }
            
            Debug.Log($"Character Switcher Setup Complete! Found {characters.Count} characters.");
        }
        
        public void SwitchToNextCharacter()
        {
            if (characters.Count <= 1) return;
            
            int nextIndex = (currentCharacterIndex + 1) % characters.Count;
            SwitchToCharacter(nextIndex);
        }
        
        public void SwitchToCharacter(int index)
        {
            if (index < 0 || index >= characters.Count) return;
            if (characters[index].characterObject == null) return;
            
            if (currentCharacterIndex < characters.Count)
            {
                DisableCharacter(currentCharacterIndex);
            }
            
            EnableCharacter(index);
            
            currentCharacterIndex = index;
            
            Debug.Log($"Switched to character: {characters[index].characterName}");
        }
        
        void DisableCharacter(int index)
        {
            if (index >= characters.Count || characters[index].characterObject == null) return;
            
            CharacterData character = characters[index];
            
            if (character.thirdPersonController != null)
            {
                character.thirdPersonController.enabled = false;
            }
            
            if (character.characterCamera != null)
            {
                character.characterCamera.SetActive(false);
            }
        }
        
        void EnableCharacter(int index)
        {
            if (index >= characters.Count || characters[index].characterObject == null) return;
            
            CharacterData character = characters[index];
            
            if (character.thirdPersonController != null)
            {
                character.thirdPersonController.enabled = true;
            }
            
            if (character.characterCamera != null)
            {
                character.characterCamera.SetActive(true);
            }
        }
        
        public int GetCurrentCharacterIndex()
        {
            return currentCharacterIndex;
        }
        
        public CharacterData GetCurrentCharacter()
        {
            if (currentCharacterIndex < characters.Count)
                return characters[currentCharacterIndex];
            return null;
        }
        
        public int GetCharacterCount()
        {
            return characters.Count;
        }
    }

    [System.Serializable]
    public class CharacterData
    {
        [Header("Character Info")]
        public string characterName = "Character";
        
        [Header("Required Components")]
        [Tooltip("The main character GameObject")]
        public GameObject characterObject;
        
        [Tooltip("The Third Person Controller component (will auto-find if empty)")]
        public MonoBehaviour thirdPersonController;
        
        [Tooltip("The character's camera GameObject (will auto-find if empty)")]
        public GameObject characterCamera;
        
        [Header("Optional Components")]
        [Tooltip("The Animator component (optional)")]
        public Animator animator;
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(CharacterSwitcherManager))]
    public class CharacterSwitcherManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            GUILayout.Space(10);
            
            CharacterSwitcherManager manager = (CharacterSwitcherManager)target;
            
            if (GUILayout.Button("🔍 Auto-Find and Populate Characters", GUILayout.Height(30)))
            {
                manager.PopulateCharactersList();
                UnityEditor.EditorUtility.SetDirty(manager);
            }
            
            GUILayout.Space(5);
            UnityEditor.EditorGUILayout.HelpBox(
                "Click the button above to automatically find all Third Person Controllers and populate the Characters list. " +
                "After populating, you can manually reorder the characters by dragging them in the list above.",
                UnityEditor.MessageType.Info
            );
        }
    }
#endif
}